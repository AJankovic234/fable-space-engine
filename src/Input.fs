namespace SpaceEngine

// =====================================================================================
//  Input.fs  -  nicht-blockierender Eingabezustand
//
//  Die Browser-Events schreiben nur in vorab angelegte Typed Arrays und Felder;
//  die Spielschleife fragt den Zustand ab, wann immer sie will (Polling). Es gibt
//  keine Event-Queue und damit keine Allokation pro Tastendruck oder Frame.
//
//  Tastatur:  W/S Pitch, A/D Yaw, Q/E Roll, Shift Boost, Leertaste Feuer,
//             Pfeil hoch/runter (oder R/F) Schub, Pfeil rechts/links Voll-/Nullschub,
//             X Bremse, L Tempo ans Ziel angleichen, T/Tab Ziel wechseln,
//             N naechstes Ziel nach Entfernung, C Kamera,
//             H Heckblick (halten), Bild auf/ab Kameraabstand, U Mausmodus, M Ton,
//             I Pitch invertieren, F1 Hilfe, F3 Statistik, F4 Schatten, Enter, P Pause
//  Maus:      Klick ins Bild aktiviert PointerLock. Die Maus bewegt dann einen
//             virtuellen Joystick (Yaw/Pitch), linke Taste feuert, rechte wechselt
//             das Ziel, das Rad regelt den Schub, die gehaltene mittlere Taste
//             sieht sich frei um, Esc gibt frei.
//             Mausmodus "Wischen": der Stick federt zur Mitte zurueck.
//             Mausmodus "Halten":  der Stick bleibt stehen (Totzone + Feinkurve).
// =====================================================================================

open Fable.Core
open Browser.Types
open Browser.Dom

/// Logische Aktionen. Als F#-Enum sind das im JavaScript blanke Zahlen und
/// damit direkt als Array-Index nutzbar.
type GameAction =
    | PitchDown = 0
    | PitchUp = 1
    | RollLeft = 2
    | RollRight = 3
    | YawLeft = 4
    | YawRight = 5
    | Boost = 6
    | Fire = 7
    | ThrottleUp = 8
    | ThrottleDown = 9
    | Brake = 10
    | ToggleCamera = 11
    | InvertPitch = 12
    | Confirm = 13
    | Pause = 14
    | CycleTarget = 15
    | ToggleMute = 16
    | UpgradeWeapon = 17
    | UpgradeShield = 18
    | UpgradeHull = 19
    | BuyTurret = 20
    | OpenShop = 21
    | StartCombat = 22
    | OpenSaves = 23
    | ScanRelic = 24
    | GhostPulse = 25
    | LaunchTorpedo = 26
    | LeaveSchool = 27
    | MouseSlower = 28
    | MouseFaster = 29
    | ThrottleFull = 30
    | ThrottleZero = 31
    | LookBack = 32
    | ToggleMouseMode = 33
    | ToggleStats = 34
    | MatchSpeed = 35
    | ZoomIn = 36
    | ZoomOut = 37
    | ToggleShadows = 38
    | ToggleHelp = 39
    | TargetNearest = 40
    | ToggleOptics = 41

type InputState =
    { /// Pro Aktion: 1 = Taste wird gerade gehalten.
      Down: JS.Uint8Array
      /// Pro Aktion: 1 = Taste wurde seit dem letzten endFrame neu gedrueckt.
      Pressed: JS.Uint8Array
      /// Virtueller Joystick der Maus, jeweils -1..1 (X rechts, Y unten).
      mutable StickX: float
      mutable StickY: float
      /// Linke Maustaste gehalten (nur bei aktivem PointerLock).
      mutable MouseFire: bool
      mutable PointerLocked: bool
      mutable UiMode: bool
      mutable SaveDialogOpen: bool
      /// true, wenn in diesem Frame ins Bild geklickt wurde (Flanke, wie Pressed).
      mutable Clicked: bool
      /// Gespeicherte Mauskalibrierung; 0.0018 entspricht auch in alten Spielständen 100 %.
      mutable MouseSensitivity: float
      /// false = "Wischen" (Stick federt zurueck), true = "Halten" (Stick bleibt stehen).
      mutable HoldStick: bool
      /// Seit dem letzten Abholen aufgelaufene Schubaenderung des Mausrads (+ = mehr Schub).
      mutable WheelThrottle: float
      /// Flanke: Mausfang oder Fensterfokus ging in diesem Frame verloren.
      mutable FocusLost: bool
      /// Mittlere Maustaste gehalten: Die Maus dreht den Blick statt des Schiffs.
      mutable MouseLook: bool
      /// Blickwinkel des freien Umsehens in Radiant (X nach rechts, Y nach unten).
      mutable LookX: float
      mutable LookY: float
      /// false: W = Nase runter, S = Nase hoch (wie ein Steuerknueppel).
      mutable InvertPitch: bool }

[<RequireQualifiedAccess>]
module Input =

    [<Literal>]
    let private ActionCount = 42

    /// Radiant Blickdrehung pro Pixel Mausweg beim freien Umsehen.
    [<Literal>]
    let private LookPerPixel = 0.004

    /// Schubaenderung pro Rastung des Mausrads.
    [<Literal>]
    let private WheelStep = 0.1

    /// Radius um die Stickmitte, in dem der Modus "Halten" nicht steuert.
    [<Literal>]
    let private HoldDeadzone = 0.06

    [<Literal>]
    let private MouseModeKey = "space2601.mouse-hold"

    // Der Mausmodus gehoert zum Geraet, nicht zum Spielstand.
    [<Emit("(() => { try { return globalThis.localStorage?.getItem($0) === '1'; } catch { return false; } })()")>]
    let private loadFlag (key: string) : bool = jsNative

    [<Emit("(() => { try { globalThis.localStorage?.setItem($0, $1 ? '1' : '0'); } catch { } })()")>]
    let private saveFlag (key: string) (value: bool) : unit = jsNative

    [<Emit("$0.addEventListener('wheel', $1, { passive: false })")>]
    let private onWheel (target: obj) (listener: Event -> unit) : unit = jsNative

    [<Emit("$0.deltaY")>]
    let private wheelDelta (event: Event) : float = jsNative

    let toggleMouseMode (state: InputState) =
        state.HoldStick <- not state.HoldStick
        state.StickX <- 0.0
        state.StickY <- 0.0
        saveFlag MouseModeKey state.HoldStick

    [<Literal>]
    let private DefaultMouseSensitivity = 0.0018

    [<Literal>]
    let private PixelsToFullStick = 140.0

    let mouseSensitivityPercent (state: InputState) =
        int (round (100.0 * state.MouseSensitivity / DefaultMouseSensitivity))

    /// Setzt die Empfindlichkeit direkt (Regler im Hauptmenue), begrenzt auf 25..250 %.
    let setMouseSensitivityPercent (state: InputState) (percent: int) =
        state.MouseSensitivity <- DefaultMouseSensitivity * float (max 25 (min 250 percent)) / 100.0

    let adjustMouseSensitivity (state: InputState) (faster: bool) =
        let current = mouseSensitivityPercent state
        let next =
            if faster then min 250 ((current / 25 + 1) * 25)
            else max 25 (((current - 1) / 25) * 25)
        if (faster && next > current) || (not faster && next < current) then
            state.MouseSensitivity <- DefaultMouseSensitivity * float next / 100.0

    /// Ordnet physische Tasten (KeyboardEvent.code, layoutunabhaengig) Aktionen zu.
    /// Liefert -1 fuer nicht belegte Tasten.
    let private actionOfCode (code: string) : int =
        match code with
        | "KeyW" -> int GameAction.PitchDown
        | "KeyS" -> int GameAction.PitchUp
        | "KeyA" -> int GameAction.YawLeft
        | "KeyD" -> int GameAction.YawRight
        | "KeyQ" -> int GameAction.RollLeft
        | "KeyE" -> int GameAction.RollRight
        | "ShiftLeft"
        | "ShiftRight" -> int GameAction.Boost
        | "Space" -> int GameAction.Fire
        | "ArrowUp"
        | "KeyR" -> int GameAction.ThrottleUp
        | "ArrowDown"
        | "KeyF" -> int GameAction.ThrottleDown
        | "KeyX" -> int GameAction.Brake
        | "KeyC" -> int GameAction.ToggleCamera
        | "KeyI" -> int GameAction.InvertPitch
        | "Enter" -> int GameAction.Confirm
        | "KeyP" -> int GameAction.Pause
        | "KeyT"
        | "Tab" -> int GameAction.CycleTarget
        | "KeyM" -> int GameAction.ToggleMute
        | "Digit1"
        | "Numpad1" -> int GameAction.BuyTurret
        | "Digit2"
        | "Numpad2" -> int GameAction.UpgradeWeapon
        | "Digit3"
        | "Numpad3" -> int GameAction.UpgradeShield
        | "Digit4"
        | "Numpad4" -> int GameAction.UpgradeHull
        | "KeyB" -> int GameAction.OpenShop
        | "F2" -> int GameAction.StartCombat
        | "F8" -> int GameAction.OpenSaves
        | "KeyG" -> int GameAction.ScanRelic
        | "KeyV" -> int GameAction.GhostPulse
        | "KeyJ" -> int GameAction.LaunchTorpedo
        | "KeyK" -> int GameAction.LeaveSchool
        | "ArrowRight" -> int GameAction.ThrottleFull
        | "ArrowLeft" -> int GameAction.ThrottleZero
        | "KeyH" -> int GameAction.LookBack
        | "KeyU" -> int GameAction.ToggleMouseMode
        | "F3" -> int GameAction.ToggleStats
        | "KeyL" -> int GameAction.MatchSpeed
        | "PageUp" -> int GameAction.ZoomIn
        | "PageDown" -> int GameAction.ZoomOut
        | "F4" -> int GameAction.ToggleShadows
        | "F1" -> int GameAction.ToggleHelp
        | "KeyO" -> int GameAction.ToggleOptics
        | "Comma" -> int GameAction.MouseSlower
        | "Period" -> int GameAction.MouseFaster
        | "KeyN" -> int GameAction.TargetNearest
        | _ -> -1

    let reset (state: InputState) =
        for i in 0 .. ActionCount - 1 do
            state.Down.[i] <- 0uy
            state.Pressed.[i] <- 0uy

        state.Clicked <- false
        state.MouseFire <- false
        state.StickX <- 0.0
        state.StickY <- 0.0
        state.WheelThrottle <- 0.0
        state.MouseLook <- false

    [<Emit("!!($0.target && ($0.target.isContentEditable || $0.target.closest?.('input, textarea, select')))")>]
    let private editing (event: Event) : bool = jsNative

    /// Legt den Zustand an und registriert alle Listener (einmalig beim Start).
    let attach (canvas: HTMLCanvasElement) : InputState =
        let state =
            { Down = JS.Constructors.Uint8Array.Create ActionCount
              Pressed = JS.Constructors.Uint8Array.Create ActionCount
              StickX = 0.0
              StickY = 0.0
              MouseFire = false
              PointerLocked = false
              UiMode = false
              SaveDialogOpen = false
              Clicked = false
              MouseSensitivity = DefaultMouseSensitivity
              HoldStick = loadFlag MouseModeKey
              WheelThrottle = 0.0
              FocusLost = false
              MouseLook = false
              LookX = 0.0
              LookY = 0.0
              InvertPitch = false }

        window.addEventListener (
            "keydown",
            fun (e: Event) ->
                let key = unbox<KeyboardEvent> e
                let action = actionOfCode key.code

                // Esc bleibt dem Browser fuer PointerLock vorbehalten, zentriert aber sofort.
                if key.code = "Escape" then
                    state.StickX <- 0.0
                    state.StickY <- 0.0

                // Browser-Kuerzel (Strg+R, Alt+Tab ...) nicht abfangen
                if action >= 0 && not (editing e) && not state.SaveDialogOpen && not (state.UiMode && key.code = "Tab") && not key.ctrlKey && not key.altKey && not key.metaKey then
                    // Auto-Repeat des Betriebssystems erzeugt kein neues "Pressed"
                    if state.Down.[action] = 0uy && not key.repeat then
                        state.Pressed.[action] <- 1uy

                    state.Down.[action] <- 1uy
                    if action = int GameAction.Brake then
                        state.StickX <- 0.0
                        state.StickY <- 0.0
                    e.preventDefault ()
        )

        window.addEventListener (
            "keyup",
            fun (e: Event) ->
                let action = actionOfCode (unbox<KeyboardEvent> e).code

                if action >= 0 then
                    state.Down.[action] <- 0uy
        )

        // Verliert das Fenster den Fokus, kaeme das keyup nie an - alles loslassen.
        window.addEventListener (
            "blur",
            fun _ ->
                reset state
                state.FocusLost <- true
        )

        onWheel canvas (fun (e: Event) ->
            if not state.UiMode then
                let delta = wheelDelta e
                if delta <> 0.0 then
                    state.WheelThrottle <- state.WheelThrottle + (if delta < 0.0 then WheelStep else -WheelStep)
                e.preventDefault ())

        canvas.addEventListener ("contextmenu", fun (e: Event) -> e.preventDefault ())

        canvas.addEventListener (
            "mousedown",
            fun (e: Event) ->
                let mouse = unbox<MouseEvent> e

                if not state.UiMode && mouse.button = 0.0 then
                    state.Clicked <- true

                if state.UiMode then ()
                elif not state.PointerLocked then
                    canvas.requestPointerLock ()
                elif mouse.button = 0.0 then
                    state.MouseFire <- true
                elif mouse.button = 2.0 then
                    state.Pressed.[int GameAction.CycleTarget] <- 1uy
                elif mouse.button = 1.0 then
                    state.MouseLook <- true
                    // Kein Autoscroll-Symbol des Browsers
                    e.preventDefault ()
        )

        window.addEventListener (
            "mouseup",
            fun (e: Event) ->
                let button = (unbox<MouseEvent> e).button

                if button = 0.0 then
                    state.MouseFire <- false
                elif button = 1.0 then
                    state.MouseLook <- false
        )

        document.addEventListener (
            "pointerlockchange",
            fun _ ->
                let wasLocked = state.PointerLocked
                state.PointerLocked <- obj.ReferenceEquals(document.pointerLockElement, canvas)

                if wasLocked && not state.PointerLocked then
                    state.FocusLost <- true

                if not state.PointerLocked then
                    state.MouseFire <- false
                    state.MouseLook <- false
                    state.StickX <- 0.0
                    state.StickY <- 0.0
        )

        document.addEventListener (
            "mousemove",
            fun (e: Event) ->
                if state.PointerLocked && not state.UiMode && state.MouseLook then
                    let mouse = unbox<MouseEvent> e
                    // Bis 150 Grad zur Seite und 70 Grad nach oben/unten
                    state.LookX <- System.Math.Max(-2.6, System.Math.Min(2.6, state.LookX + mouse.movementX * LookPerPixel))
                    state.LookY <- System.Math.Max(-1.2, System.Math.Min(1.2, state.LookY + mouse.movementY * LookPerPixel))
                elif state.PointerLocked && not state.UiMode then
                    let mouse = unbox<MouseEvent> e
                    let perPixel = state.MouseSensitivity / (DefaultMouseSensitivity * PixelsToFullStick)
                    let mutable x = state.StickX + mouse.movementX * perPixel
                    let mutable y = state.StickY + mouse.movementY * perPixel
                    // Auf den Einheitskreis begrenzen
                    let lengthSq = x * x + y * y

                    if lengthSq > 1.0 then
                        let inv = 1.0 / sqrt lengthSq
                        x <- x * inv
                        y <- y * inv

                    state.StickX <- x
                    state.StickY <- y
        )

        state

    /// Wird die Aktion gerade gehalten?
    let inline isDown (state: InputState) (action: GameAction) : bool = state.Down.[int action] <> 0uy

    /// Wurde die Aktion in diesem Frame neu ausgeloest (Flanke)?
    let inline wasPressed (state: InputState) (action: GameAction) : bool = state.Pressed.[int action] <> 0uy

    /// Am Ende jedes Frames aufrufen: setzt die Flanken (Tasten und Klick) zurueck.
    let endFrame (state: InputState) =
        for i in 0 .. ActionCount - 1 do
            state.Pressed.[i] <- 0uy

        state.Clicked <- false
        state.FocusLost <- false

    /// Einmal pro Frame: Nach dem Loslassen der mittleren Taste schwenkt der Blick
    /// zurueck nach vorn.
    let relaxLook (state: InputState) (dt: float) =
        if not state.MouseLook && (state.LookX <> 0.0 || state.LookY <> 0.0) && dt > 0.0 then
            let keep = exp (-9.0 * dt)
            let x = state.LookX * keep
            let y = state.LookY * keep

            if x * x + y * y < 1e-6 then
                state.LookX <- 0.0
                state.LookY <- 0.0
            else
                state.LookX <- x
                state.LookY <- y

    /// Holt die seit dem letzten Aufruf aufgelaufene Schubaenderung des Mausrads ab.
    let takeWheelThrottle (state: InputState) : float =
        let amount = state.WheelThrottle
        state.WheelThrottle <- 0.0
        amount

    let inline private held (state: InputState) (action: GameAction) : float = float state.Down.[int action]

    let private clampUnit (v: float) : float =
        if v > 1.0 then 1.0
        elif v < -1.0 then -1.0
        else v

    let private mouseAxis (state: InputState) (axis: float) : float =
        // Gleicher Faktor für beide Achsen erhält die Richtung schräger Mausbewegungen.
        let radiusSq = state.StickX * state.StickX + state.StickY * state.StickY

        if state.HoldStick then
            // Der Stick bleibt stehen: Eine Totzone erlaubt Geradeausflug, die Kurve
            // (Exponent 1,5) macht kleine Ausschlaege fein und laesst den Vollausschlag bei 1.
            let radius = sqrt radiusSq

            if radius <= HoldDeadzone then
                0.0
            else
                let t = System.Math.Min(1.0, (radius - HoldDeadzone) / (1.0 - HoldDeadzone))
                axis * (t * sqrt t / radius)
        else
            axis * (0.72 + 0.28 * radiusSq)

    /// Nach dem Anwenden der Eingabe aufrufen. Exponentielle Rueckstellung bleibt
    /// unabhaengig von der Physikfrequenz; die Bremse setzt den Stick sofort auf null.
    /// Im Modus "Halten" zentriert nur die Bremse.
    let centerStick (state: InputState) (dt: float) =
        if state.Down.[int GameAction.Brake] <> 0uy then
            state.StickX <- 0.0
            state.StickY <- 0.0
        elif dt > 0.0 && not state.HoldStick then
            let damping = exp (-4.5 * dt)
            let x = state.StickX * damping
            let y = state.StickY * damping
            if x * x + y * y < 0.001 * 0.001 then
                state.StickX <- 0.0
                state.StickY <- 0.0
            else
                state.StickX <- x
                state.StickY <- y

    /// Pitch-Befehl in -1..1, positiv = Nase hoch.
    let pitch (state: InputState) : float =
        let keys = held state GameAction.PitchUp - held state GameAction.PitchDown
        // Maus nach vorn (StickY negativ) hebt die Nase
        let command = keys - mouseAxis state state.StickY
        clampUnit (if state.InvertPitch then -command else command)

    /// Yaw-Befehl in -1..1, positiv = Nase nach links.
    let yaw (state: InputState) : float =
        clampUnit (
            held state GameAction.YawLeft
            - held state GameAction.YawRight
            - mouseAxis state state.StickX
        )

    /// Roll-Befehl in -1..1, positiv = Rolle nach links.
    let roll (state: InputState) : float =
        held state GameAction.RollLeft - held state GameAction.RollRight

    /// Schubaenderung in -1..1 (Pfeiltasten bzw. R/F).
    let throttleChange (state: InputState) : float =
        held state GameAction.ThrottleUp - held state GameAction.ThrottleDown

    /// Feuerbefehl aus Tastatur oder Maus.
    let fire (state: InputState) : bool =
        state.Down.[int GameAction.Fire] <> 0uy || state.MouseFire
