module SpaceEngine.GraphicsQuality

type Tier = Low | Medium | High | Ultra

type Preset =
    { RenderScale: float
      DprLimit: float
      Msaa: bool
      Shadows: bool
      ShadowSize: int
      BloomLevels: int
      SmallObjectDistance: float
      PointLights: int
      Particles: int }

let private high =
    { RenderScale = 1.0; DprLimit = 2.0; Msaa = true; Shadows = true
      ShadowSize = 2048; BloomLevels = 3; SmallObjectDistance = infinity
      PointLights = 16; Particles = 12288 }

let private low =
    { RenderScale = 0.70; DprLimit = 1.0; Msaa = false; Shadows = false
      ShadowSize = 512; BloomLevels = 1; SmallObjectDistance = 950.0
      PointLights = 4; Particles = 3072 }

let private medium =
    { RenderScale = 0.85; DprLimit = 1.5; Msaa = false; Shadows = true
      ShadowSize = 1024; BloomLevels = 2; SmallObjectDistance = 1800.0
      PointLights = 8; Particles = 6144 }

let private ultra =
    { high with DprLimit = infinity; ShadowSize = 4096; PointLights = 32; Particles = 24576 }

let preset = function Low -> low | Medium -> medium | High -> high | Ultra -> ultra
let label = function Low -> "Niedrig" | Medium -> "Mittel" | High -> "Hoch" | Ultra -> "Ultra"
let key = function Low -> "low" | Medium -> "medium" | High -> "high" | Ultra -> "ultra"
let parse = function "low" -> Some Low | "medium" -> Some Medium | "high" -> Some High | "ultra" -> Some Ultra | _ -> None

let validFrame ms = ms > 0.0 && ms < 500.0

// Ein einzelner Ausreisser (z.B. Shader-Kompilierung) soll kein Fenster dominieren.
let frameMean total frames worst =
    if frames >= 4 then (total - worst) / float (frames - 1)
    else total / float frames

/// Gemessen wird bei Hoch im Flug. Auf 60-Hz-Anzeigen bleibt Hoch der Normalfall.
let autoTier meanMs =
    if meanMs < 10.0 then Ultra
    elif meanMs <= 18.0 then High
    elif meanMs <= 29.0 then Medium
    else Low

type Resolution = { Scale: float; LastIncrease: float; RecoverAfter: float }

let resolution scale = { Scale = scale; LastIncrease = -infinity; RecoverAfter = 0.0 }

/// Gleitendes Fenster aus acht Teilmitteln, neuestes zuerst, 0 = noch unbesetzt.
/// Unter Last nach zwei langsamen Teilfenstern senken. Erst nach acht stabilen
/// Teilfenstern anheben. 60-Hz-VSync darf eine Erholung zulassen.
let adjustScale maximum now (means: float[]) state =
    let step delta = max 0.5 (min maximum (round ((state.Scale + delta) * 100.0) / 100.0))
    if state.Scale > 0.5 && means.[0] > 18.2 && means.[1] > 18.2 then
        // War die letzte Erhöhung zu teuer, erst nach 15 s den nächsten Versuch wagen.
        let retry = if now - state.LastIncrease < 1600.0 then now + 15000.0 else state.RecoverAfter
        { state with Scale = step -0.05; RecoverAfter = retry }
    elif state.Scale < maximum && now >= state.RecoverAfter
         && (means |> Array.forall (fun ms -> ms > 0.0 && ms <= 17.0)) then
        { state with Scale = step 0.05; LastIncrease = now }
    else state
