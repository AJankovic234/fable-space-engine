module SpaceEngine.GraphicsSettings

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

[<Emit("(() => { try { return localStorage.getItem($0) } catch { return null } })()")>]
let private read (key: string) : string = jsNative

[<Emit("(() => { try { if ($1 == null) localStorage.removeItem($0); else localStorage.setItem($0,$1) } catch {} })()")>]
let private write (key: string) (value: string) : unit = jsNative

let private tierKey = "space2601.graphics-tier"
let private dynamicKey = "space2601.dynamic-resolution"
let private shadowKey = "space2601.shadows"

/// Hält nur Zeiten tatsächlich gezeichneter Flugframes. Der Messpuffer und die
/// GL-Ziele bleiben beim Sammeln der Messwerte erhalten.
type Settings(renderer: Renderer) =
    let selector = document.getElementById "graphics-tier"
    let toggle = unbox<HTMLInputElement> (document.getElementById "graphics-dynamic")
    let output = document.getElementById "graphics-scale"
    let autoOption = document.getElementById "graphics-auto"
    let choice = GraphicsQuality.parse (read tierKey)
    let mutable measuring = Option.isNone choice
    let mutable tier = defaultArg choice GraphicsQuality.High
    let mutable preset = GraphicsQuality.preset tier
    let mutable dynamic = read dynamicKey <> "0"
    let mutable shadowOverride = read shadowKey
    let means = Array.zeroCreate<float> 8
    let mutable elapsed = 0.0
    let mutable count = 0
    let mutable worst = 0.0
    let mutable warmup = 0.0
    let mutable active = false
    let mutable flightMs = 0.0
    let mutable resolution = GraphicsQuality.resolution preset.RenderScale

    let resetSamples delay =
        Array.fill means 0 means.Length 0.0
        elapsed <- 0.0
        count <- 0
        worst <- 0.0
        warmup <- delay

    let showScale () = output.textContent <- string (int (round (100.0 * renderer.RenderScale))) + " %"

    let apply level =
        tier <- level
        preset <- GraphicsQuality.preset level
        let withShadows =
            match shadowOverride with
            | "0" -> { preset with Shadows = false }
            | "1" -> { preset with Shadows = true }
            | _ -> preset
        Renderer.applyQuality renderer withShadows preset.RenderScale
        resolution <- GraphicsQuality.resolution preset.RenderScale
        resetSamples 500.0
        showScale ()

    do
        apply tier
        selector?value <- choice |> Option.map GraphicsQuality.key |> Option.defaultValue "auto"
        toggle.``checked`` <- dynamic
        selector.addEventListener("change", fun _ ->
            let value = string selector?value
            let selected = GraphicsQuality.parse value
            measuring <- Option.isNone selected
            autoOption.textContent <- "Auto"
            shadowOverride <- null
            write shadowKey null
            write tierKey value
            apply (defaultArg selected GraphicsQuality.High))
        toggle.addEventListener("change", fun _ ->
            dynamic <- toggle.``checked``
            write dynamicKey (if dynamic then "1" else "0")
            renderer.RenderScale <- preset.RenderScale
            resolution <- GraphicsQuality.resolution preset.RenderScale
            resetSamples 500.0
            showScale ())
        document.addEventListener("visibilitychange", fun _ -> active <- false)

    member _.Tier = tier
    member _.Preset = preset
    member _.ToggleShadows() =
        let enabled = Renderer.toggleShadows renderer
        shadowOverride <- if enabled then "1" else "0"
        write shadowKey shadowOverride
        enabled

    member _.Frame(flying, ms) =
        if not flying || document?visibilityState = "hidden" then active <- false
        elif not active then
            active <- true
            resetSamples 500.0
        elif (measuring || dynamic) && GraphicsQuality.validFrame ms then
            if warmup > 0.0 then warmup <- warmup - ms
            else
                elapsed <- elapsed + ms
                worst <- max worst ms
                count <- count + 1
                flightMs <- flightMs + ms
                if elapsed >= 320.0 then
                    for i = means.Length - 1 downto 1 do means.[i] <- means.[i-1]
                    means.[0] <- GraphicsQuality.frameMean elapsed count worst
                    elapsed <- 0.0
                    worst <- 0.0
                    count <- 0
                    if measuring then
                        if means.[means.Length - 1] > 0.0 then
                            measuring <- false
                            apply (GraphicsQuality.autoTier (Array.average means))
                            autoOption.textContent <- "Auto (" + GraphicsQuality.label tier + ")"
                    else
                        resolution <- GraphicsQuality.adjustScale preset.RenderScale flightMs means resolution
                        if resolution.Scale <> renderer.RenderScale then
                            renderer.RenderScale <- resolution.Scale
                            resetSamples 180.0
                            showScale ()
