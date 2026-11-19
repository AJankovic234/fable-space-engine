namespace SpaceEngine

// =====================================================================================
//  HUD.fs  -  2D-Overlay auf einem eigenen Canvas ueber dem WebGL-Canvas
//
//  Inhalt im Flug: Fadenkreuz, Zielerfassung (Zielrahmen, Entfernung, Vorhalte-
//  marke), Pfeile am Bildrand fuer Gegner ausserhalb des Sichtfelds, Schild/Huelle,
//  Tempo/Schub/Boost, Welle/Gegner/Punkte, Meldungen und Treffer-Feedback.
//  Dazu die Bildschirme fuer Menue, Pause und Game Over.
//
//  Performance
//    * Gezeichnet wird in CSS-Pixeln; eine einzige Transformation skaliert auf die
//      physische Aufloesung.
//    * Zahlen 0..999 liegen als fertige Strings in einer Tabelle (Tempo, Prozente,
//      Entfernungen) - das Zeichnen pro Frame erzeugt dafuer keine neuen Strings.
//      Groessere Werte (Punkte) werden nur neu formatiert, wenn sie sich aendern.
//    * Die 3D->2D-Projektion laeuft skalar in Scratch-Felder des HUD-Objekts.
// =====================================================================================

open System
open Fable.Core
open Browser.Types

type Hud =
    { Canvas: HTMLCanvasElement
      Ctx: CanvasRenderingContext2D
      /// Vorformatierte Strings "0" .. "999".
      Numbers: string[]
      /// Groesse in CSS-Pixeln und Verhaeltnis physische/CSS-Pixel.
      mutable Width: float
      mutable Height: float
      mutable Ratio: float
      /// Roter Randverlauf fuer das Treffer-Feedback (wird bei Groessenaenderung neu erzeugt).
      mutable Vignette: CanvasGradient
      // --- Scratch der Projektion ---
      mutable ClipX: float
      mutable ClipY: float
      mutable ClipW: float
      mutable ScreenX: float
      mutable ScreenY: float
      // --- Zwischengespeicherte Texte ---
      mutable ScoreValue: int
      mutable ScoreText: string
      mutable StatsText: string
      /// Texte der Vollbild-Seiten; werden beim Betreten des Zustands einmal gesetzt.
      mutable BestText: string
      mutable HasSave: bool
      mutable SaveText: string
      mutable SaveFailed: bool
      mutable ResultScoreText: string
      mutable ResultDetailText: string
      mutable ResultBestText: string
      mutable ResultIsBest: bool
      mutable ResultVictory: bool
      mutable StageName: string
      mutable Objective: string
      mutable StageProgress: float
      mutable PendingEnemies: int
      mutable Countdown: int
      mutable Credits: int
      mutable CargoUsed: int
      mutable CargoCapacity: int
      mutable FreightGate: string option
      mutable CourseGate: string option
      mutable Trading: bool
      mutable Trips: int
      mutable TradeProfit: int
      mutable TurretOwned: bool
      mutable TurretTarget: int
      mutable MissileAmmo: int
      mutable MissileCapacity: int
      mutable MissileReserve: int
      mutable MissileReload: float
      mutable ScannerAddon: bool
      mutable Salvaging: bool
      mutable RewardText: string
      mutable RewardTime: float
      // --- Meldungen und Effekte ---
      mutable Banner: string
      mutable BannerSub: string
      mutable BannerTime: float
      mutable BannerDuration: float
      mutable RadioCallsign: string
      mutable RadioText: string
      mutable RadioFactionTag: int
      mutable RadioPan: float
      mutable RadioTime: float
      mutable RadioDuration: float
      /// > 0: Treffermarke am Fadenkreuz sichtbar (eigener Laser hat getroffen).
      mutable HitMarker: float
      /// 0..1: roter Bildrand nach erlittenem Schaden.
      mutable DamageFlash: float
      /// Index des erfassten Gegners oder -1.
      mutable Target: int
      /// true, wenn das Ziel manuell per T/Tab/RMB aufgeschaltet wurde.
      mutable ManualLock: bool
      /// Aktueller Kill-Kombo-Multiplikator (1..4) und Restzeit.
      mutable Combo: int
      mutable ComboTimer: float
      mutable Muted: bool
      /// Boost-Vorrat 0..1 und Sperre nach dem Leerfliegen (vom Aufrufer pro Frame gesetzt).
      mutable BoostReserve: float
      mutable BoostLocked: bool
      /// Mausmodus fuer Pausentext und Stick-Anzeige.
      mutable HoldStick: bool
      /// Schub ist an das Tempo des Ziels gekoppelt.
      mutable MatchSpeed: bool
      Reserved: ResizeArray<HudLayout.Rect>
      Markers: ResizeArray<HudContacts.Marker>
      mutable InCombat: bool
      mutable Time: float
      /// Letzter eigener Treffer lag auf dem Schild (blaue statt helle Treffermarke).
      mutable HitShield: bool
      /// > 0: Abschussmarke am Fadenkreuz (klingt ab).
      mutable KillMarker: float
      /// Trefferrichtungen: je Platz [x, y, z, Restzeit], Richtung in Weltkoordinaten.
      DamageDirs: float[]
      /// Dargestellte Kamerarotation des Frames (fuer die Trefferrichtung).
      mutable ViewRotation: Quat }

[<RequireQualifiedAccess>]
module Hud =

    let private receiverPanel = lazy (
        let byId id = Browser.Dom.document.getElementById id
        byId "recovery-instrument", byId "recovery-title", byId "recovery-instruction",
        byId "recovery-trace", Browser.Dom.document.querySelector(".recovery-meter i") :?> HTMLElement)

    let private lessonPanel = lazy (
        let byId id = Browser.Dom.document.getElementById id
        byId "flight-school", byId "school-progress", byId "school-title", byId "school-instruction")

    let showSchool step =
        let panel,progress,title,instruction = lessonPanel.Value
        if step = FlightSchool.Off then panel.setAttribute ("hidden","")
        else
            let heading,keys =
                match step with
                | FlightSchool.Accelerate -> "Nimm Fahrt auf", "Halte <kbd>↑</kbd> oder <kbd>R</kbd>, bis du 18 m/s erreichst."
                | FlightSchool.Brake -> "Bremse dein Schiff ab", "<kbd>↓</kbd> senkt den Schub. Halte jetzt <kbd>X</kbd>, bis du unter 4 m/s bist."
                | FlightSchool.FirstTarget -> "Triff die Zieldrohne", "Ziele mit <kbd>A</kbd>/<kbd>D</kbd> und <kbd>W</kbd>/<kbd>S</kbd>. Feuere mit der <kbd>Leertaste</kbd>. Die Drohne ist unbewaffnet."
                | FlightSchool.MovingTarget -> "Folge dem beweglichen Ziel", "Die zweite Drohne pendelt seitwärts. Nimm sie ins Fadenkreuz und halte die <kbd>Leertaste</kbd>."
                | _ -> "Zurück ans Handelsdock", "Flieg mit <kbd>↑</kbd> zur Stationsmarke. Unter 90 m kannst du mit <kbd>B</kbd> andocken."
            title.textContent <- heading
            instruction.innerHTML <- keys
            progress.textContent <- string step + "/5"
            panel.removeAttribute "hidden"

    let hideRecovery () =
        let root,_,_,_,_ = receiverPanel.Value
        if not root.hidden then root.hidden <- true

    let showRecovery title instruction trace meter =
        let root,head,action,line,bar = receiverPanel.Value
        if root.hidden then root.hidden <- false
        if head.textContent <> title then head.textContent <- title
        if action.textContent <> instruction then action.textContent <- instruction
        if line.textContent <> trace then line.textContent <- trace
        let style = "transform:scaleX(" + string (round (100.0 * max 0.0 (min 1.0 meter)) / 100.0) + ")"
        if bar.getAttribute "style" <> style then bar.setAttribute ("style", style)

    [<Literal>]
    let private Cyan = "rgba(120, 230, 255, 0.92)"

    [<Literal>]
    let private CyanDim = "rgba(120, 230, 255, 0.16)"

    [<Literal>]
    let private TextColor = "rgba(205, 220, 240, 0.85)"

    [<Literal>]
    let private TextDim = "rgba(205, 220, 240, 0.5)"

    [<Literal>]
    let private Red = "rgba(255, 90, 70, 0.95)"

    [<Literal>]
    let private RedSoft = "rgba(255, 90, 70, 0.62)"

    [<Literal>]
    let private Amber = "rgba(255, 200, 100, 0.95)"

    [<Literal>]
    let private Green = "rgba(130, 240, 150, 0.95)"

    [<Literal>]
    let private ShieldBlue = "rgba(90, 170, 255, 0.95)"

    [<Literal>]
    let private FontSmall = "12px ui-monospace, Consolas, monospace"

    [<Literal>]
    let private FontLabel = "600 12px ui-monospace, Consolas, monospace"

    [<Literal>]
    let private FontValue = "600 22px ui-monospace, Consolas, monospace"

    [<Literal>]
    let private FontBanner = "600 30px ui-monospace, Consolas, monospace"

    [<Literal>]
    let private FontTitle = "600 46px ui-monospace, Consolas, monospace"

    /// Abstand der Randpfeile vom Bildschirmrand (CSS-Pixel).
    [<Literal>]
    let private EdgeMargin = 46.0

    /// Bis zu dieser Entfernung wird eine Vorhaltemarke angezeigt (Laser-Reichweite).
    [<Literal>]
    let private LeadRange = 640.0

    /// Gleichzeitig angezeigte Trefferrichtungen.
    [<Literal>]
    let private DamageSlots = 4

    [<Literal>]
    let private HitMarkerTime = 0.22

    [<Literal>]
    let private KillMarkerTime = 0.6

    let inline private fill (ctx: CanvasRenderingContext2D) (color: string) = ctx.fillStyle <- U3.Case1 color
    let inline private stroke (ctx: CanvasRenderingContext2D) (color: string) = ctx.strokeStyle <- U3.Case1 color

    let create (canvas: HTMLCanvasElement) : Hud =
        { Canvas = canvas
          Ctx = canvas.getContext_2d ()
          Numbers = Array.init 1000 string
          Width = 1.0
          Height = 1.0
          Ratio = 1.0
          Vignette = Unchecked.defaultof<CanvasGradient>
          ClipX = 0.0
          ClipY = 0.0
          ClipW = 0.0
          ScreenX = 0.0
          ScreenY = 0.0
          ScoreValue = -1
          ScoreText = "0"
          StatsText = ""
          BestText = ""
          HasSave = false
          SaveText = ""
          SaveFailed = false
          ResultScoreText = ""
          ResultDetailText = ""
          ResultBestText = ""
          ResultIsBest = false
          ResultVictory = false
          StageName = ""
          Objective = ""
          StageProgress = 0.0
          PendingEnemies = 0
          Countdown = 0
          Credits = 0
          CargoUsed = 0
          CargoCapacity = 16
          FreightGate = None
          CourseGate = None
          Trading = true
          Trips = 0
          TradeProfit = 0
          TurretOwned = false
          TurretTarget = -1
          MissileAmmo = Missiles.MaxAmmo
          MissileCapacity = Missiles.MaxAmmo
          MissileReserve = 0
          MissileReload = 0.0
          ScannerAddon = false
          Salvaging = false
          RewardText = ""
          RewardTime = 0.0
          Banner = ""
          BannerSub = ""
          BannerTime = 0.0
          BannerDuration = 1.0
          RadioCallsign = ""
          RadioText = ""
          RadioFactionTag = 0
          RadioPan = 0.0
          RadioTime = 0.0
          RadioDuration = 1.0
          HitMarker = 0.0
          DamageFlash = 0.0
          Target = -1
          ManualLock = false
          Combo = 1
          ComboTimer = 0.0
          Muted = false
          BoostReserve = 1.0
          BoostLocked = false
          HoldStick = false
          MatchSpeed = false
          Reserved = ResizeArray()
          Markers = ResizeArray()
          InCombat = false
          Time = 0.0
          HitShield = false
          KillMarker = 0.0
          DamageDirs = Array.zeroCreate (4 * DamageSlots)
          ViewRotation = Quat.identity }

    /// Haelt das HUD-Canvas deckungsgleich mit dem WebGL-Canvas. Nur bei
    /// Groessenaenderung aufrufen (setzt die Canvas-Groesse, was den Inhalt loescht).
    let resize (hud: Hud) (pixelWidth: int) (pixelHeight: int) (ratio: float) =
        hud.Canvas.width <- float pixelWidth
        hud.Canvas.height <- float pixelHeight
        hud.Ratio <- ratio
        hud.Width <- float pixelWidth / ratio
        hud.Height <- float pixelHeight / ratio
        let cx = hud.Width * 0.5
        let cy = hud.Height * 0.5
        let outer = sqrt (cx * cx + cy * cy)
        let gradient = hud.Ctx.createRadialGradient (cx, cy, outer * 0.55, cx, cy, outer)
        gradient.addColorStop (0.0, "rgba(255, 40, 20, 0)")
        gradient.addColorStop (1.0, "rgba(255, 40, 20, 0.85)")
        hud.Vignette <- gradient

    /// Zeigt eine Meldung in der oberen Bildmitte.
    let notify (hud: Hud) (text: string) (sub: string) (duration: float) =
        hud.Banner <- text
        hud.BannerSub <- sub
        hud.BannerTime <- duration
        hud.BannerDuration <- duration

    /// Zeigt einen laufenden Fraktions-Funkspruch als kompakten Ticker im Cockpit-HUD.
    let showRadio (hud: Hud) (callsign: string) (text: string) (factionTag: int) (pan: float) (duration: float) =
        hud.RadioCallsign <- callsign
        hud.RadioText <- text
        hud.RadioFactionTag <- factionTag
        hud.RadioPan <- pan
        hud.RadioTime <- duration
        hud.RadioDuration <- max 0.5 duration

    /// Eigener Laser hat ein Schiff getroffen.
    let hitMarker (hud: Hud) = hud.HitMarker <- HitMarkerTime

    /// Treffermarke mit Unterscheidung Schild (blau) / Huelle (hell).
    let confirmHit (hud: Hud) (shield: bool) =
        hud.HitMarker <- HitMarkerTime
        hud.HitShield <- shield

    /// Abschussbestaetigung: rotes Kreuz und Ring am Fadenkreuz.
    let confirmKill (hud: Hud) =
        hud.KillMarker <- KillMarkerTime
        hud.HitMarker <- 0.0

    /// Merkt eine Trefferrichtung (Welt, vom Spieler zur Quelle). Treffer aus fast
    /// derselben Richtung frischen ihren Platz auf, sonst wird der aelteste ersetzt.
    let damageFrom (hud: Hud) (direction: Vec3) =
        let d = hud.DamageDirs
        let mutable slot = -1
        let mutable oldest = 0
        for i in 0 .. DamageSlots - 1 do
            let o = i * 4
            if d.[o + 3] < d.[oldest * 4 + 3] then oldest <- i
            if slot < 0 && d.[o + 3] > 0.0
               && d.[o] * direction.X + d.[o + 1] * direction.Y + d.[o + 2] * direction.Z > 0.94 then
                slot <- i
        let o = (if slot >= 0 then slot else oldest) * 4
        d.[o] <- direction.X
        d.[o + 1] <- direction.Y
        d.[o + 2] <- direction.Z
        d.[o + 3] <- CombatFeedback.DamageIndicatorTime

    /// Der Spieler hat Schaden erlitten.
    let damage (hud: Hud) (amount: float) =
        hud.DamageFlash <- Math.Min(1.0, hud.DamageFlash + 0.25 + amount * 0.03)

    /// Statistikzeile (FPS usw.); wird vom Aufrufer nur einige Male pro Sekunde gesetzt.
    let setStats (hud: Hud) (text: string) = hud.StatsText <- text

    /// Bestwert fuer den Titelbildschirm (0 = noch keiner).
    let setBest (hud: Hud) (best: int) =
        hud.BestText <- (if best > 0 then "Bestwert  " + string best else "")

    /// Ergebnis fuer den Game-Over-Bildschirm. Einmal beim Spielende aufrufen -
    /// die Texte werden hier formatiert, nicht in jedem Frame.
    let setResult (hud: Hud) (score: int) (wave: int) (kills: int) (best: int) (isBest: bool) =
        hud.ResultScoreText <- string score + " Punkte"
        hud.ResultDetailText <- "Sektor " + string wave + " / 6   ::   " + string kills + " Abschüsse"
        hud.ResultBestText <- (if isBest then "Neuer Bestwert!" else "Bestwert  " + string best)
        hud.ResultIsBest <- isBest

    /// Laesst Meldungen und Effekte abklingen (einmal pro Frame).
    let update (hud: Hud) (dt: float) =
        hud.Time <- hud.Time + dt
        hud.RewardTime <- max 0.0 (hud.RewardTime - dt)
        hud.BannerTime <- Math.Max(0.0, hud.BannerTime - dt)
        hud.RadioTime <- Math.Max(0.0, hud.RadioTime - dt)
        hud.HitMarker <- Math.Max(0.0, hud.HitMarker - dt)
        hud.KillMarker <- Math.Max(0.0, hud.KillMarker - dt)
        hud.DamageFlash <- Math.Max(0.0, hud.DamageFlash - dt * 1.6)
        for i in 0 .. DamageSlots - 1 do
            let o = i * 4 + 3
            if hud.DamageDirs.[o] > 0.0 then hud.DamageDirs.[o] <- Math.Max(0.0, hud.DamageDirs.[o] - dt)

        if hud.ComboTimer > 0.0 then
            hud.ComboTimer <- Math.Max(0.0, hud.ComboTimer - dt)

            if hud.ComboTimer <= 0.0 then
                hud.Combo <- 1

    /// Schaltet manuell auf den naechsten lebenden Gegner weiter.
    let cycleTarget (hud: Hud) (enemies: Ship[]) : bool =
        let n = enemies.Length
        let start = if hud.Target >= 0 then hud.Target + 1 else 0
        let mutable found = -1

        for step in 0 .. n - 1 do
            let idx = (start + step) % n

            if found < 0 && enemies.[idx].Alive then
                found <- idx

        if found >= 0 then
            hud.Target <- found
            hud.ManualLock <- true
            true
        else
            hud.Target <- -1
            hud.ManualLock <- false
            false

    /// Schaltet auf das naechstgelegene Ziel, bei erneutem Druck auf das naechstfernere (N).
    let targetNearest (hud: Hud) (enemies: Ship[]) (position: Vec3) : bool =
        let next = CombatFeedback.nextByDistance enemies position (if hud.ManualLock then hud.Target else -1)
        if next >= 0 then
            hud.Target <- next
            hud.ManualLock <- true
            true
        else
            false

    /// Loescht das Overlay und stellt die Zeichen-Transformation ein.
    /// Erster Aufruf jedes Frames, vor allen draw*-Funktionen.
    let clear (hud: Hud) =
        let ctx = hud.Ctx
        ctx.setTransform (hud.Ratio, 0.0, 0.0, hud.Ratio, 0.0, 0.0)
        ctx.clearRect (0.0, 0.0, hud.Width, hud.Height)
        hud.Reserved.Clear()
        hud.Markers.Clear()

    let private queueMarker (hud: Hud) glyph x y radius tint priority quiet title detail =
        hud.Markers.Add
            { Glyph = glyph; X = x; Y = y; Radius = if quiet then min 5.0 radius else radius
              Tint = tint; Priority = priority; Quiet = quiet; Title = title; Detail = detail }

    let drawWorldMarkers (hud: Hud) =
        HudContacts.draw hud.Ctx hud.Width hud.Height hud.Reserved hud.Markers

    /// String fuer eine Zahl aus der Tabelle (auf 0..999 begrenzt).
    let private number (hud: Hud) (value: float) : string =
        let n = int (value + 0.5)
        hud.Numbers.[(if n < 0 then 0 elif n > 999 then 999 else n)]

    /// Projiziert einen Weltpunkt: Clip-Koordinaten und (falls vor der Kamera)
    /// Bildschirmkoordinaten landen in den Scratch-Feldern des HUD.
    let private project (hud: Hud) (viewProj: Mat4) (x: float) (y: float) (z: float) =
        let m = viewProj.Data
        let cx = float m.[0] * x + float m.[4] * y + float m.[8] * z + float m.[12]
        let cy = float m.[1] * x + float m.[5] * y + float m.[9] * z + float m.[13]
        let cw = float m.[3] * x + float m.[7] * y + float m.[11] * z + float m.[15]
        hud.ClipX <- cx
        hud.ClipY <- cy
        hud.ClipW <- cw

        if cw > 1e-3 then
            hud.ScreenX <- (cx / cw * 0.5 + 0.5) * hud.Width
            hud.ScreenY <- (0.5 - cy / cw * 0.5) * hud.Height

    /// Liegt der zuletzt projizierte Punkt sichtbar im Bild?
    let private onScreen (hud: Hud) : bool =
        hud.ClipW > 1e-3
        && hud.ScreenX >= 0.0
        && hud.ScreenX <= hud.Width
        && hud.ScreenY >= 0.0
        && hud.ScreenY <= hud.Height

    /// Vier Eckwinkel um (x, y).
    let private bracket (ctx: CanvasRenderingContext2D) (x: float) (y: float) (size: float) (corner: float) =
        ctx.beginPath ()
        ctx.moveTo (x - size, y - size + corner)
        ctx.lineTo (x - size, y - size)
        ctx.lineTo (x - size + corner, y - size)
        ctx.moveTo (x + size - corner, y - size)
        ctx.lineTo (x + size, y - size)
        ctx.lineTo (x + size, y - size + corner)
        ctx.moveTo (x + size, y + size - corner)
        ctx.lineTo (x + size, y + size)
        ctx.lineTo (x + size - corner, y + size)
        ctx.moveTo (x - size + corner, y + size)
        ctx.lineTo (x - size, y + size)
        ctx.lineTo (x - size, y + size - corner)
        ctx.stroke ()

    /// Gefuelltes Dreieck bei (x, y), Spitze in Richtung (dx, dy) (normalisiert).
    let private arrow (ctx: CanvasRenderingContext2D) (x: float) (y: float) (dx: float) (dy: float) (size: float) =
        // Senkrechte zur Pfeilrichtung
        let px = -dy
        let py = dx
        ctx.beginPath ()
        ctx.moveTo (x + dx * size, y + dy * size)
        ctx.lineTo (x - dx * size * 0.6 + px * size * 0.75, y - dy * size * 0.6 + py * size * 0.75)
        ctx.lineTo (x - dx * size * 0.6 - px * size * 0.75, y - dy * size * 0.6 - py * size * 0.75)
        ctx.closePath ()
        ctx.fill ()

    /// Beschrifteter Balken: Hintergrund plus gefuellter Anteil 0..1.
    let private bar
        (ctx: CanvasRenderingContext2D)
        (x: float)
        (y: float)
        (width: float)
        (height: float)
        (fraction: float)
        (color: string)
        =
        fill ctx CyanDim
        ctx.fillRect (x, y, width, height)
        fill ctx color
        ctx.fillRect (x, y, width * Math.Max(0.0, Math.Min(1.0, fraction)), height)

    /// Zeichnet die Cockpit-Kanzelstreben in der Ich-Perspektive (inkl. feiner Scheibenrisse bei schwerem Huellenschaden).
    let private drawCockpitCanopy (ctx: CanvasRenderingContext2D) (w: float) (h: float) (hullFraction: float) =
        stroke ctx "rgba(26, 48, 66, 0.85)"
        ctx.lineWidth <- 10.0
        ctx.beginPath ()
        // Obere und untere Kanzelstreben
        ctx.moveTo (w * 0.14, 0.0)
        ctx.lineTo (w * 0.25, h * 0.16)
        ctx.lineTo (w * 0.18, h)
        ctx.moveTo (w * 0.86, 0.0)
        ctx.lineTo (w * 0.75, h * 0.16)
        ctx.lineTo (w * 0.82, h)
        ctx.moveTo (w * 0.25, h * 0.16)
        ctx.lineTo (w * 0.75, h * 0.16)
        ctx.stroke ()

        stroke ctx CyanDim
        ctx.lineWidth <- 1.5
        ctx.beginPath ()
        ctx.moveTo (w * 0.145, 0.0)
        ctx.lineTo (w * 0.254, h * 0.16)
        ctx.lineTo (w * 0.184, h)
        ctx.moveTo (w * 0.855, 0.0)
        ctx.lineTo (w * 0.746, h * 0.16)
        ctx.lineTo (w * 0.816, h)
        ctx.stroke ()

        // Armaturenbrett: traegt Statusblock, Radar und Wertung als Instrumente.
        // Feste Pixelhoehen, weil die Instrumente ebenfalls am unteren Rand verankert sind.
        let edge = h - 198.0
        let dip = h - 142.0
        let hump = h - 178.0
        let console () =
            ctx.beginPath ()
            ctx.moveTo (0.0, edge)
            ctx.lineTo (w * 0.24, edge + 10.0)
            ctx.lineTo (w * 0.33, dip)
            ctx.lineTo (w * 0.40, dip)
            ctx.lineTo (w * 0.43, hump)
            ctx.lineTo (w * 0.57, hump)
            ctx.lineTo (w * 0.60, dip)
            ctx.lineTo (w * 0.67, dip)
            ctx.lineTo (w * 0.76, edge + 10.0)
            ctx.lineTo (w, edge)
        console ()
        ctx.lineTo (w, h)
        ctx.lineTo (0.0, h)
        ctx.closePath ()
        fill ctx "rgba(6, 13, 21, 0.93)"
        ctx.fill ()
        // Blendschutz-Kante mit schwachem Instrumentenlicht
        console ()
        stroke ctx "rgba(26, 48, 66, 0.95)"
        ctx.lineWidth <- 7.0
        ctx.stroke ()
        console ()
        stroke ctx "rgba(118, 229, 249, 0.34)"
        ctx.lineWidth <- 1.2
        ctx.stroke ()
        // Kontrollleuchten links und rechts der Mittelkonsole
        for k in 0 .. 3 do
            let offset = w * 0.345 + float k * w * 0.012
            fill ctx (if k = 0 && hullFraction < 0.3 then "rgba(255, 84, 60, 0.9)" else "rgba(118, 229, 249, 0.42)")
            ctx.fillRect (offset, dip + 9.0, w * 0.007, 3.0)
            ctx.fillRect (w - offset - w * 0.007, dip + 9.0, w * 0.007, 3.0)

        if hullFraction < 0.45 then
            let crackAlpha = Math.Min(0.55, (0.45 - hullFraction) * 1.4 + 0.12)
            stroke ctx ("rgba(180, 235, 255, " + crackAlpha.ToString("0.00") + ")")
            ctx.lineWidth <- 1.1
            ctx.beginPath ()
            ctx.moveTo (w * 0.24, h * 0.16)
            ctx.lineTo (w * 0.18, h * 0.24)
            ctx.lineTo (w * 0.14, h * 0.29)
            ctx.moveTo (w * 0.18, h * 0.24)
            ctx.lineTo (w * 0.21, h * 0.31)
            if hullFraction < 0.25 then
                ctx.moveTo (w * 0.76, h * 0.16)
                ctx.lineTo (w * 0.83, h * 0.27)
                ctx.lineTo (w * 0.80, h * 0.35)
                ctx.moveTo (w * 0.83, h * 0.27)
                ctx.lineTo (w * 0.88, h * 0.31)
            ctx.stroke ()

    /// 3D-Sensorschirm (Gravidar) unten in der Bildmitte: zeigt Gegner und nahe
    /// Gross-Asteroiden relativ zur Ausrichtung des eigenen Schiffs mit Hoehenlinien.
    let private drawRadar
        (hud: Hud)
        (world: World)
        (sector: SectorDef)
        (school: int)
        (sortie: bool)
        (scanElapsed: float)
        (scanRange: float)
        (alpha: float)
        (playerPosition: Vec3)
        (playerRotation: Quat)
        (target: int)
        =
        let ctx = hud.Ctx
        let cx = hud.Width * 0.5
        let cy = hud.Height - 66.0
        let rx = 74.0
        let ry = 30.0
        // Mit Demodulator teilen sich Kontakte und Scanwelle die erweiterte Skala.
        let radarRange = max 460.0 scanRange

        // Hintergrund-Gehaeuse
        fill ctx "rgba(3, 12, 23, 0.74)"
        ctx.fillRect (cx - 92.0, cy - 52.0, 184.0, 102.0)
        stroke ctx CyanDim
        ctx.lineWidth <- 1.0
        ctx.strokeRect (cx - 92.0, cy - 52.0, 184.0, 102.0)
        fill ctx Cyan
        ctx.fillRect (cx - 18.0, cy - 52.0, 36.0, 2.0)

        // Radarscheibe (geneigte Ellipse: y = x * ry/rx)
        ctx.save ()
        ctx.translate (cx, cy + 4.0)
        ctx.scale (1.0, ry / rx)
        fill ctx "rgba(8, 28, 46, 0.55)"
        ctx.beginPath ()
        ctx.arc (0.0, 0.0, rx, 0.0, 2.0 * Math.PI)
        ctx.fill ()
        stroke ctx "rgba(120, 230, 255, 0.35)"
        ctx.lineWidth <- 1.5
        ctx.stroke ()
        stroke ctx CyanDim
        ctx.beginPath ()
        ctx.arc (0.0, 0.0, rx * 0.5, 0.0, 2.0 * Math.PI)
        ctx.moveTo (-rx, 0.0)
        ctx.lineTo (rx, 0.0)
        ctx.moveTo (0.0, -rx)
        ctx.lineTo (0.0, rx)
        // Blickkegel nach vorn (oben auf der Scheibe = -Z)
        ctx.moveTo (0.0, 0.0)
        ctx.lineTo (-rx * 0.62, -rx * 0.78)
        ctx.moveTo (0.0, 0.0)
        ctx.lineTo (rx * 0.62, -rx * 0.78)
        ctx.stroke ()
        if scanRange > 0.0 && scanElapsed >= 0.0 && scanElapsed < Recovery.pulseDuration then
            let progress = scanElapsed / Recovery.pulseDuration
            let pulseRadius = rx * (scanRange / radarRange) * min 1.0 (progress / 0.8)
            ctx.globalAlpha <- min 1.0 ((1.0 - progress) / 0.2)
            stroke ctx "#deabf3"
            ctx.lineWidth <- 3.0
            ctx.beginPath ()
            ctx.arc (0.0, 0.0, pulseRadius, 0.0, 2.0 * Math.PI)
            ctx.stroke ()
        ctx.restore ()

        // Konjugierte Schiffs-Quaternion fuer allokationsfreie Welt->Lokal-Rotation
        let qx = -playerRotation.X
        let qy = -playerRotation.Y
        let qz = -playerRotation.Z
        let qw = playerRotation.W
        let baseY = cy + 4.0

        // 1. Nahe, groessere Asteroiden als dezente Punkte auf der Scheibe
        let field = world.Asteroids
        let ad = field.Data
        fill ctx "rgba(130, 175, 200, 0.42)"

        for i in 0 .. field.Count - 1 do
            let o = i * AsteroidField.Stride
            let radius = float ad.[o + 6]

            if radius >= 9.0 then
                let vx = float ad.[o] - playerPosition.X
                let vy = float ad.[o + 1] - playerPosition.Y
                let vz = float ad.[o + 2] - playerPosition.Z

                if vx * vx + vy * vy + vz * vz < radarRange * radarRange then
                    let tx = 2.0 * (qy * vz - qz * vy)
                    let ty = 2.0 * (qz * vx - qx * vz)
                    let tz = 2.0 * (qx * vy - qy * vx)
                    let lx = (vx + qw * tx + (qy * tz - qz * ty)) / radarRange
                    let lz = (vz + qw * tz + (qx * ty - qy * tx)) / radarRange
                    let px = cx + lx * rx
                    let py = baseY + lz * ry
                    ctx.fillRect (px - 1.2, py - 1.2, 2.4, 2.4)

        // Gleiche Projektion fuer Navigations- und Kampfkontakte. Kurzer Fussstrich
        // auf der Scheibe, Symbol am oberen bzw. unteren Ende der Hoehenlinie.
        let toLocal = Quat.conjugate playerRotation
        let contact p tint glyph size core =
            let local = Quat.rotateVec3 toLocal (p-playerPosition)
            let struct (offset,foot,tip) = HudLayout.radarOffset radarRange rx ry local
            let x,y = cx+offset,baseY+tip
            stroke ctx tint
            ctx.lineWidth <- 1.2
            if abs (tip-foot) >= 1.0 then
                ctx.beginPath ()
                ctx.moveTo (x-2.0,baseY+foot)
                ctx.lineTo (x+2.0,baseY+foot)
                ctx.moveTo (x,baseY+foot)
                ctx.lineTo (x,y)
                ctx.stroke ()
            HudContacts.symbol ctx glyph x y size
            if core > 0.0 then
                fill ctx tint
                ctx.beginPath ()
                ctx.arc (x,y,core,0.0,2.0*Math.PI)
                ctx.fill ()

        if not sortie && (school = FlightSchool.Off || school = FlightSchool.Dock) then
            contact sector.Outpost.Position Cyan HudContacts.Square 3.3 0.0
            if school = FlightSchool.Off then
                for gate in sector.Gates do
                    if hud.FreightGate = Some gate.TargetSectorId then
                        contact gate.Position Green HudContacts.CargoCrate 5.0 0.0
                    elif hud.CourseGate = Some gate.TargetSectorId then
                        contact gate.Position Cyan HudContacts.Diamond 5.0 0.0
                    else
                        contact gate.Position "#ccb36f" HudContacts.Diamond 3.5 0.0
                if hud.Trading then
                    for site in WreckFields.forSector world.SectorIndex do
                        contact site.Position Amber HudContacts.CargoCrate 3.8 0.0

        let enemies = world.Enemies
        for i in 0 .. enemies.Length-1 do
            if i <> target && enemies.[i].Alive then
                let enemy = enemies.[i]
                contact (Vec3.lerp enemy.PrevPosition enemy.Position alpha) Red HudContacts.Circle 2.3 2.3
        if target >= 0 then
            let enemy = enemies.[target]
            let p = Vec3.lerp enemy.PrevPosition enemy.Position alpha
            contact p "#ffce79" HudContacts.Circle 5.2 1.6

        // Eigenes Schiff in der Mitte
        fill ctx Cyan
        ctx.fillRect (cx - 2.0, baseY - 2.0, 4.0, 4.0)
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText ("GRAVIDAR · " + number hud radarRange + " m", cx, cy - 46.0)
        if scanRange > 0.0 then
            fill ctx "#deabf3"
            ctx.fillText ("[G] Archivscan · " + number hud scanRange + " m", cx, cy + 35.0)

    let drawCustoms (hud: Hud) (vp: Mat4) (blockade: BlockadeState) (f: CapitalFlightState) (player: Vec3) freightSix =
        let ctx = hud.Ctx
        let marker (p: Vec3) tint label size priority =
            project hud vp p.X p.Y p.Z
            let quiet = hud.InCombat && f.Role = 0
            if onScreen hud then
                queueMarker hud HudContacts.Square hud.ScreenX hud.ScreenY size tint priority quiet label ""
            elif f.Role <> 0 && p = CapitalFlight.position then
                let length = max 0.0001 (sqrt (hud.ClipX*hud.ClipX + hud.ClipY*hud.ClipY))
                let dx,dy = hud.ClipX/length,-hud.ClipY/length
                let x = hud.Width*0.5 + dx*max 20.0 (hud.Width*0.5-45.0)
                let y = hud.Height*0.5 + dy*max 20.0 (hud.Height*0.5-130.0)
                queueMarker hud (HudContacts.Bearing(dx,dy)) x y 9.0 tint priority false label ""
        ctx.save ()
        ctx.textBaseline <- "top"
        if f.Role = 0 && Vec3.distance player freightSix < 490.0 then
            let clearance =
                if f.Landing >= CapitalFlight.landingTime then "am Liegeplatz"
                elif blockade.Lifted || blockade.Permit = 2 then "Einflug frei"
                else "wartet auf Freigabe"
            marker freightSix Cyan ("Frachter Sechs · " + clearance) 8.0 60
        let distance = Vec3.distance player CapitalFlight.position
        if not blockade.Lifted then
            marker CapitalFlight.position Amber ("AETHER · Tariff VII · " + string (int distance) + " m") 11.0 (if f.Role <> 0 then 120 else 75)
            if f.Role <> 0 && distance < 460.0 then
                for i in 0..1 do
                    if f.Guns.[i] > 0.0 then marker (CapitalFlight.position+CapitalFlight.gunOffsets.[i]) Red "" 5.5 125
        if f.Role = CapitalFlight.Escort && not blockade.Lifted then
            marker (CapitalFlight.allyPosition f) Green "Kepler Zwei" 8.0 110
        if f.Role <> 0 && not blockade.Lifted then
            let width = min 356.0 (hud.Width - 32.0)
            fill ctx "rgba(7,16,22,0.92)"
            ctx.fillRect (16.0,123.0,width,63.0)
            hud.Reserved.Add(HudLayout.rect 16.0 123.0 width 63.0)
            ctx.textAlign <- "left"
            fill ctx Amber
            ctx.font <- FontLabel
            let loadout =
                if f.Role = CapitalFlight.Bomber then sprintf "TORPEDOS  %d/2" f.Ammo
                else sprintf "KEPLER ZWEI  %d %% HÜLLE" (int (ceil f.WingHull))
            ctx.fillText (loadout + "     PANZERUNG  " + string (2-f.Hits) + "/2",25.0,131.0,width-18.0)
            let hint =
                if f.Flight > 0.0 then "Torpedo unterwegs"
                elif f.Role = CapitalFlight.Escort then
                    if f.Defeated <> 3 then "Abfangjäger ausschalten · rote Batterien mit Laser"
                    else "Bomber im Anflug · Rückendeckung halten"
                elif distance > CapitalFlight.range then "Auf höchstens 420 m heranfliegen"
                elif distance < 35.0 then "Zu nah · Abstand gewinnen"
                elif f.Lock >= CapitalFlight.lockTime then "Ziel erfasst · J zum Abschuss"
                else "Kreuzer im Visier halten · X bremst"
            fill ctx TextColor
            ctx.font <- FontSmall
            ctx.fillText (hint,25.0,151.0,width-18.0)
            bar ctx 25.0 174.0 (width-18.0) 3.0 (if f.Flight > 0.0 then 1.0-f.Flight else f.Lock/CapitalFlight.lockTime) Amber
        ctx.restore ()

    let drawStation (hud: Hud) (viewProj: Mat4) (aspect: float) (outpost: Outpost) (position: Vec3) (dockBlocked: bool) =
        let p = outpost.Position
        let distance = Vec3.distance p position
        project hud viewProj p.X p.Y p.Z
        let x,y,glyph =
            if onScreen hud then hud.ScreenX,hud.ScreenY,HudContacts.Square
            else
                let dx,dy = hud.ClipX / max 0.01 aspect,-hud.ClipY
                let length = sqrt (dx*dx + dy*dy)
                let nx,ny = if length < 0.001 then 0.0,1.0 else dx/length,dy/length
                hud.Width*0.5 + nx*max 16.0 (hud.Width*0.5-30.0),
                hud.Height*0.5 + ny*max 16.0 (hud.Height*0.5-186.0),HudContacts.Bearing(nx,ny)
        let docking = Universe.evaluateDocking outpost position dockBlocked
        let prompt =
            match docking with
            | ReadyToDock _ -> "[B] Andocken · Handel & Ausrüstung"
            | BlockedByCombat _ -> "Dock gesperrt · Sektor sichern"
            | _ -> "Anfliegen · Dockradius " + string (int outpost.DockRadius) + " m"
        match docking with
        | ReadyToDock _ when hud.InCombat ->
            queueMarker hud glyph x y 5.0 Cyan 100 false ("[B] Andocken · " + string (int distance) + " m") ""
        | _ ->
            queueMarker hud glyph x y 10.0 Cyan 100 hud.InCombat (outpost.Name + " · " + string (int distance) + " m") prompt

    /// Short streaks expand from the projected direction of travel after a jump.
    let drawJumpTransit (hud: Hud) (viewProj: Mat4) (position: Vec3) (direction: Vec3) progress =
        let vanishing = position + direction * 1000.0
        project hud viewProj vanishing.X vanishing.Y vanishing.Z
        if hud.ClipW > 1e-3 then
            let x, y = hud.ScreenX, hud.ScreenY
            let strength = (1.0 - progress) ** 1.6
            let ctx = hud.Ctx
            ctx.save ()
            stroke ctx "#bceaff"
            ctx.lineWidth <- 1.1
            for i in 0 .. 63 do
                let angle = float i * 2.39996323
                let seed = float ((i * 47 + 13) % 67) / 67.0
                let distance = 0.14 + seed * 0.57 + progress * (0.28 + 0.67 * seed)
                let tail = distance - strength * (0.024 + seed * 0.13)
                let dx, dy = cos angle * hud.Width, sin angle * hud.Height
                ctx.globalAlpha <- strength * (0.3 + 0.6 * seed)
                ctx.beginPath ()
                ctx.moveTo(x + dx * tail, y + dy * tail)
                ctx.lineTo(x + dx * distance, y + dy * distance)
                ctx.stroke ()
            ctx.restore ()

    /// Markiert die Sprungtore des aktuellen Sektors im 3D-Raum mit Zielnamen und Distanz.
    let drawJumpGates (hud: Hud) (viewProj: Mat4) (sector: SectorDef) (position: Vec3) =
        for gate in sector.Gates do
            let distance = Vec3.distance gate.Position position
            // Nah am Tor rückt die Marke über den Ring, damit die Durchfahrt frei bleibt.
            let p = if distance < 120.0 then gate.Position + Quat.up (Universe.gateRotation sector gate) * 30.0 else gate.Position
            project hud viewProj p.X p.Y p.Z
            if onScreen hud then
                let title = gate.Label + " · " + string (int distance) + " m"
                if hud.FreightGate = Some gate.TargetSectorId then
                    queueMarker hud HudContacts.CargoCrate hud.ScreenX hud.ScreenY 9.0 Green 75 hud.InCombat ("Frachtkurs · " + title) ""
                elif hud.CourseGate = Some gate.TargetSectorId then
                    queueMarker hud HudContacts.Diamond hud.ScreenX hud.ScreenY 9.0 Cyan 72 hud.InCombat ("Kurs · " + title) ""
                else
                    queueMarker hud HudContacts.Diamond hud.ScreenX hud.ScreenY 8.0 Amber 50 hud.InCombat title ""

    /// A seized liner broadcasts locally. Ordinary contacts still require looking at the hull.
    let drawTraffic (hud: Hud) viewProj (fleet: Traffic.Fleet) position =
        if not hud.InCombat then
            let radius = min 90.0 (hud.Height*0.13)
            let mutable best = radius*radius
            let mutable contact = None
            let mutable sx,sy = 0.0,0.0
            for entry in fleet.Contacts do
                match entry with
                | Some ship ->
                    let p = ship.Position
                    let reach = if ship.Broadcast then 850.0 else 600.0
                    if Vec3.distanceSq p position <= reach*reach then
                        project hud viewProj p.X p.Y p.Z
                        if onScreen hud then
                            if ship.Broadcast then
                                queueMarker hud HudContacts.Circle hud.ScreenX hud.ScreenY 5.0 Amber 43 false ship.Name ship.Detail
                            else
                                let dx,dy = hud.ScreenX-hud.Width*0.5, hud.ScreenY-hud.Height*0.5
                                let score = dx*dx + dy*dy
                                if score < best then
                                    best <- score
                                    contact <- entry
                                    sx <- hud.ScreenX
                                    sy <- hud.ScreenY
                | None -> ()
            match contact with
            | Some ship ->
                let tint = if Traffic.models.[int ship.Class].Aether then Amber else Cyan
                let distance = int (Vec3.distance ship.Position position)
                queueMarker hud HudContacts.Circle sx sy 4.0 tint 36 false ship.Name (ship.Detail + " · " + string distance + " m")
            | None -> ()

    let drawWrecks (hud: Hud) (viewProj: Mat4) sector recoveries (position: Vec3) =
        for site in WreckFields.forSector sector do
            let distance = Vec3.distance site.Position position
            if distance <= WreckFields.MarkerRange then
                let p = site.Position + Vec3(0.0, 13.0, 0.0)
                project hud viewProj p.X p.Y p.Z
                if onScreen hud then
                    let stock = WreckFields.remaining recoveries site
                    let title = site.Name + " · " + string (int distance) + " m"
                    let voice = WreckVoices.state stock.Id
                    let tint, detail =
                        if not (WreckFields.hasLoot stock) then
                            CyanDim, (match voice with
                                      | Kept -> "Geborgen · Stimme im Archiv"
                                      | Cut -> "Geborgen · Notruf gekappt"
                                      | Sending -> "Bereits geborgen")
                        elif distance <= WreckFields.SalvageRange then
                            match voice with
                            | Sending ->
                                Amber, sprintf "Notruf wird aufgezeichnet %d %% · [G] kappen und bergen" (int (100.0 * WreckVoices.recorded stock.Id))
                            | Kept -> Amber, "Stimme gesichert · [G] Bergen"
                            | Cut -> Amber, "[G] Bergen"
                        elif voice = Sending then Amber, "Sendet noch · Bergung ab 65 m"
                        else Amber, "Bergung ab 65 m"
                    queueMarker hud HudContacts.CargoCrate hud.ScreenX hud.ScreenY 8.0 tint 65 (hud.InCombat && distance > WreckFields.SalvageRange) title detail

    /// Das Flug-HUD.
    ///   viewProj/aspect : Kamera des aktuellen Frames
    ///   alpha           : Interpolationsfaktor zwischen den letzten Physikzustaenden
    ///   playerPosition/-Rotation : interpolierte Darstellungspose des Spielers
    let drawFlight
        (hud: Hud)
        (viewProj: Mat4)
        (aspect: float)
        (cameraMode: CameraMode)
        (opticsActive: bool)
        (world: World)
        (brains: AiBrain[])
        (sectorIndex: int)
        (pickups: ResizeArray<MineralPickup>)
        (scanElapsed: float)
        (input: InputState)
        (alpha: float)
        (playerPosition: Vec3)
        (playerRotation: Quat)
        (wave: int)
        (score: int)
        (enemiesAlive: int)
        (sortie: bool)
        (school: int)
        =
        let ctx = hud.Ctx
        let w = hud.Width
        let h = hud.Height
        let player = world.Player
        let enemies = world.Enemies
        let centerX = w * 0.5
        let centerY = h * 0.5

        if cameraMode = Cockpit && player.Alive then
            let hullFrac = if player.MaxHull > 0.0 then player.Hull / player.MaxHull else 1.0
            drawCockpitCanopy ctx w h hullFrac

        // ---- Radiale Boost-Speedlines am Bildrand ----
        if player.Alive && player.Boost > 0.15 then
            let b = Math.Min(1.0, player.Boost)
            let maxR = Math.Sqrt(centerX * centerX + centerY * centerY)
            ctx.save ()
            stroke ctx ("rgba(118, 229, 249, " + (0.14 + b * 0.22).ToString("0.00") + ")")
            ctx.lineWidth <- 1.3
            ctx.beginPath ()
            for k in 0 .. 17 do
                let ang = float k * (2.0 * Math.PI / 18.0) + 0.08 * sin (hud.Time * 3.0 + float k)
                let inner = maxR * (0.76 - 0.08 * b + 0.04 * sin (hud.Time * 19.0 + float k * 2.1))
                let outer = maxR * 0.99
                ctx.moveTo (centerX + cos ang * inner, centerY + sin ang * inner)
                ctx.lineTo (centerX + cos ang * outer, centerY + sin ang * outer)
            ctx.stroke ()
            ctx.restore ()

        // ---- Treffer-Feedback & kritische Huellen-Warnung: roter Bildrand ----
        let lowHullPulse =
            if player.Alive && player.MaxHull > 0.0 && player.Hull < player.MaxHull * 0.35 then
                0.16 + 0.12 * sin (hud.Time * 6.0)
            else
                0.0
        let edgeAlpha = Math.Max(hud.DamageFlash, lowHullPulse)
        if edgeAlpha > 0.01 then
            ctx.globalAlpha <- edgeAlpha
            ctx.fillStyle <- U3.Case2 hud.Vignette
            ctx.fillRect (0.0, 0.0, w, h)
            ctx.globalAlpha <- 1.0

        // ---- Trefferrichtung: rote Boegen um die Bildmitte zeigen zur Schadensquelle ----
        if player.Alive then
            let toView = Quat.conjugate hud.ViewRotation
            let radius = Math.Min(w, h) * 0.22
            let d = hud.DamageDirs
            ctx.save ()
            ctx.lineCap <- "round"
            for slot in 0 .. DamageSlots - 1 do
                let o = slot * 4
                let life = d.[o + 3]
                if life > 0.0 then
                    let local = Quat.rotateVec3 toView (Vec3(d.[o], d.[o + 1], d.[o + 2]))
                    let struct (angle, weight) = CombatFeedback.indicator local.X local.Y local.Z
                    // Frisch kraeftig, in der letzten halben Sekunde ausblenden
                    let fresh = (life - (CombatFeedback.DamageIndicatorTime - 0.25)) / 0.25
                    ctx.globalAlpha <- Math.Min(1.0, life / 0.5) * weight
                    stroke ctx "rgba(255, 60, 40, 0.32)"
                    ctx.lineWidth <- 14.0 + 6.0 * Math.Max(0.0, fresh)
                    ctx.beginPath ()
                    ctx.arc (centerX, centerY, radius, angle - 0.36, angle + 0.36)
                    ctx.stroke ()
                    stroke ctx Red
                    ctx.lineWidth <- 3.0
                    ctx.beginPath ()
                    ctx.arc (centerX, centerY, radius + 6.0, angle - 0.3, angle + 0.3)
                    ctx.stroke ()
                    fill ctx Red
                    let ux = cos angle
                    let uy = sin angle
                    arrow ctx (centerX + ux * (radius + 20.0)) (centerY + uy * (radius + 20.0)) ux uy 8.0
            ctx.restore ()

        // ---- Zielpunkt des Schiffs (Fadenkreuz) ----
        let forward = Quat.forward playerRotation
        // In der Verfolgerkamera entsteht bei nahen Zielen Parallaxe. Das Visier
        // zeigt die Schussachse auf Zieltiefe, ohne die Geschosse zu lenken.
        let muzzle = playerPosition + Quat.rotateVec3 playerRotation ((player.GunLeft + player.GunRight) * 0.5)
        let aimDistance =
            if hud.Target >= 0 && hud.Target < enemies.Length && enemies.[hud.Target].Alive then
                let depth = Vec3.dot (enemies.[hud.Target].Position - muzzle) forward
                if depth > 5.0 then min 800.0 depth else 800.0
            else 800.0
        let sight = muzzle + forward * aimDistance
        project hud viewProj sight.X sight.Y sight.Z
        let aimVisible = hud.ClipW > 1e-3
        let aimX = if aimVisible then hud.ScreenX else centerX
        let aimY = if aimVisible then hud.ScreenY else centerY

        let sightBounds =
            let r = if cameraMode = Cockpit then 68.0 else 38.0
            HudLayout.rect (aimX-r) (aimY-38.0) (r + (if opticsActive then 112.0 else r)) 76.0
        hud.Reserved.Add sightBounds

        // ---- Prograde-Drift-Marke (tatsaechlicher Geschwindigkeitsvektor) ----
        let speed = Vec3.length player.Velocity

        if player.Alive && speed > 6.0 then
            project
                hud
                viewProj
                (playerPosition.X + player.Velocity.X * 12.0)
                (playerPosition.Y + player.Velocity.Y * 12.0)
                (playerPosition.Z + player.Velocity.Z * 12.0)

            if onScreen hud then
                let vx = hud.ScreenX
                let vy = hud.ScreenY
                stroke ctx "rgba(120, 230, 255, 0.52)"
                ctx.lineWidth <- 1.3
                ctx.beginPath ()
                ctx.arc (vx, vy, 5.0, 0.0, 2.0 * Math.PI)
                ctx.moveTo (vx - 10.0, vy)
                ctx.lineTo (vx - 5.0, vy)
                ctx.moveTo (vx + 5.0, vy)
                ctx.lineTo (vx + 10.0, vy)
                ctx.moveTo (vx, vy - 10.0)
                ctx.lineTo (vx, vy - 5.0)
                ctx.stroke ()

        // ---- Zielerfassung: manuell gewaehltes Ziel bleibt bestehen, solange es lebt;
        //      sonst der Gegner, der dem Fadenkreuz am naechsten liegt.
        let mutable target = -1

        if hud.ManualLock && hud.Target >= 0 && hud.Target < enemies.Length && enemies.[hud.Target].Alive then
            target <- hud.Target
        else
            hud.ManualLock <- false
            let mutable bestScore = Double.MaxValue

            for i in 0 .. enemies.Length - 1 do
                let enemy = enemies.[i]

                if enemy.Alive then
                    let ex = enemy.PrevPosition.X + (enemy.Position.X - enemy.PrevPosition.X) * alpha
                    let ey = enemy.PrevPosition.Y + (enemy.Position.Y - enemy.PrevPosition.Y) * alpha
                    let ez = enemy.PrevPosition.Z + (enemy.Position.Z - enemy.PrevPosition.Z) * alpha
                    project hud viewProj ex ey ez
                    let dx = ex - playerPosition.X
                    let dy = ey - playerPosition.Y
                    let dz = ez - playerPosition.Z
                    let distance = sqrt (dx * dx + dy * dy + dz * dz)

                    let mutable candidate =
                        if onScreen hud then
                            let sx = hud.ScreenX - aimX
                            let sy = hud.ScreenY - aimY
                            sqrt (sx * sx + sy * sy)
                        else
                            10000.0 + distance

                    if i = hud.Target then
                        candidate <- candidate * 0.6

                    if candidate < bestScore then
                        bestScore <- candidate
                        target <- i

        hud.Target <- target

        hud.InCombat <- sortie || (target >= 0 && enemies |> Array.exists (fun e ->
            e.Alive && Vec3.distanceSq e.Position playerPosition <= LeadRange*LeadRange))
        let sector = Universe.sectorByIndex sectorIndex
        // Archivposition erst nach erfolgreichem Scan einblenden.
        if school = FlightSchool.Off && PilotSkills.contains world.Pilot.Relics sectorIndex then
            let p = RelicArchive.position sector.Outpost.Position sectorIndex
            let distance = Vec3.distance p playerPosition
            if distance <= RelicArchive.range world.Pilot then
                project hud viewProj p.X p.Y p.Z
                if onScreen hud then
                    queueMarker hud HudContacts.Circle hud.ScreenX hud.ScreenY 7.0 "#deabf3" 65 hud.InCombat
                        ("Archiv · " + fst RelicArchive.entries.[sectorIndex]) (string (int distance) + " m")
        if school = FlightSchool.Off && scanElapsed >= 0.0 && scanElapsed <= 5.5 then
            let scanRange = RelicArchive.range world.Pilot + (if hud.ScannerAddon then 250.0 else 0.0)
            let maxPings = if hud.ScannerAddon then 12 else 6
            let scanRangeSq = scanRange * scanRange
            let asteroids = world.Asteroids
            let data = asteroids.Data
            let mutable shown = 0
            for i in 0 .. asteroids.Count - 1 do
                let ore = int asteroids.Ore.[i]
                if ore > 0 && shown < maxPings then
                    let o = i * 16
                    let ax = float data.[o]
                    let ay = float data.[o + 1]
                    let az = float data.[o + 2]
                    let dx = ax - playerPosition.X
                    let dy = ay - playerPosition.Y
                    let dz = az - playerPosition.Z
                    let dSq = dx * dx + dy * dy + dz * dz
                    if dSq <= scanRangeSq then
                        project hud viewProj ax ay az
                        if onScreen hud then
                            let tint = if ore = 2 then Amber else Cyan
                            let tag = if ore = 2 then "ERZ+ · " else "ERZ · "
                            queueMarker hud HudContacts.Diamond hud.ScreenX hud.ScreenY 5.5 tint 32 hud.InCombat
                                (tag + number hud (sqrt dSq) + " m") ""
                            shown <- shown + 1
        for pickup in pickups do
            let p = pickup.Position
            project hud viewProj p.X p.Y p.Z
            if onScreen hud then
                let tint, prefix =
                    match pickup.Kind with
                    | 2 -> Amber, "ERZ+ · "
                    | 3 -> "#deabf3", "NAV · "
                    | 4 -> "#cbd8e3", "TIT · "
                    | 5 -> "#ff9b4a", "PLS · "
                    | 6 -> "#f3c66b", "TRB · "
                    | _ -> Cyan, ""
                queueMarker hud HudContacts.Diamond hud.ScreenX hud.ScreenY 6.0 tint 35 hud.InCombat
                    (prefix + number hud (Vec3.distance p playerPosition) + " m") ""

        // ---- Gegner-Marker: Rahmen im Bild, Pfeile am Rand ----
        ctx.textAlign <- "center"
        ctx.textBaseline <- "middle"
        ctx.font <- FontSmall
        let mutable targetX = 0.0
        let mutable targetY = 0.0
        let mutable targetVisible = false

        let drawEnemy i =
            let enemy = enemies.[i]

            if enemy.Alive then
                let ex = enemy.PrevPosition.X + (enemy.Position.X - enemy.PrevPosition.X) * alpha
                let ey = enemy.PrevPosition.Y + (enemy.Position.Y - enemy.PrevPosition.Y) * alpha
                let ez = enemy.PrevPosition.Z + (enemy.Position.Z - enemy.PrevPosition.Z) * alpha
                project hud viewProj ex ey ez
                let dx = ex - playerPosition.X
                let dy = ey - playerPosition.Y
                let dz = ez - playerPosition.Z
                let distance = sqrt (dx * dx + dy * dy + dz * dz)
                let isTarget = i = target

                let attacking =
                    match brains.[i].State with
                    | AiState.Attack -> true
                    | _ -> false

                let profile = EnemyProfiles.profile brains.[i].Role

                if onScreen hud then
                    let x = hud.ScreenX
                    let y = hud.ScreenY
                    let apparent = enemy.Radius * 0.72 * h / hud.ClipW
                    let size = if isTarget then max 23.0 (min 94.0 apparent) else max 7.0 (min 23.0 apparent)
                    let struck = enemy.HitFlash > 0.35
                    let charging = brains.[i].WeaponCharge > 0.0 && brains.[i].Role >= EnemyRole.Gunship
                    hud.Reserved.Add(HudLayout.around x y (size+7.0))
                    if isTarget then
                        targetX <- x
                        targetY <- y
                        targetVisible <- true
                        stroke ctx "rgba(5,12,18,0.9)"
                        ctx.lineWidth <- 5.0
                        bracket ctx x y size (size*0.38)
                    ctx.globalAlpha <- if isTarget || struck then 1.0 else 0.68
                    stroke ctx (if struck then "#ffffff" elif isTarget then "#ffce79" elif FlightSchool.onRange school then Green else profile.Color)
                    ctx.lineWidth <- if isTarget then 2.8 else 1.25
                    bracket ctx x y size (size*0.38)
                    ctx.globalAlpha <- 1.0
                    if charging || isTarget then
                        fill ctx (if charging then "#ff9155" else "#ffce79")
                        arrow ctx x (y-size-3.0) 0.0 1.0 (if isTarget then 6.0 else 3.5)
                else
                    // Bildschirmrichtung zum Gegner. Die Clip-Koordinaten VOR der Division
                    // durch w zeigen auch fuer Ziele hinter der Kamera in die richtige
                    // Richtung; aspect entzerrt die Projektion.
                    let mutable sx = hud.ClipX * aspect
                    let mutable sy = -hud.ClipY
                    let len = sqrt (sx * sx + sy * sy)

                    if len > 1e-6 then
                        sx <- sx / len
                        sy <- sy / len
                    else
                        // Genau hinter dem Spieler: nach unten zeigen
                        sx <- 0.0
                        sy <- 1.0

                    // Vom Bildmittelpunkt in Pfeilrichtung bis an den (eingerueckten) Rand
                    let limitX = centerX - EdgeMargin
                    // Nach unten frueher stoppen: dort stehen Statusblock, Radar und Wertung.
                    let limitY = if sy > 0.0 then Math.Max(60.0, centerY - 186.0) else centerY - EdgeMargin

                    let scale =
                        Math.Min(
                            (if abs sx > 1e-6 then limitX / abs sx else Double.MaxValue),
                            (if abs sy > 1e-6 then limitY / abs sy else Double.MaxValue)
                        )

                    let x = centerX + sx * scale
                    let y = centerY + sy * scale
                    // Angreifer pulsieren, nahe Gegner bekommen groessere Pfeile
                    let pulse = if attacking then 0.75 + 0.25 * sin (hud.Time * 12.0) else 1.0
                    let size = if isTarget then 14.0 else (if distance < 250.0 then 9.5 else 7.0) * pulse
                    fill ctx (if isTarget then "#ffce79" elif FlightSchool.onRange school then Green elif attacking then Red else RedSoft)
                    arrow ctx x y sx sy size
                    hud.Reserved.Add(HudLayout.around x y (size+2.0))

        for i in 0 .. enemies.Length-1 do
            if i <> target then drawEnemy i
        if target >= 0 then drawEnemy target

        // ---- Vorhaltemarke: Wohin zielen, damit der Bolt das Ziel trifft? ----
        if target >= 0 && player.Alive then
            let enemy = enemies.[target]
            let ex = enemy.PrevPosition.X + (enemy.Position.X - enemy.PrevPosition.X) * alpha
            let ey = enemy.PrevPosition.Y + (enemy.Position.Y - enemy.PrevPosition.Y) * alpha
            let ez = enemy.PrevPosition.Z + (enemy.Position.Z - enemy.PrevPosition.Z) * alpha
            let px = ex - playerPosition.X
            let py = ey - playerPosition.Y
            let pz = ez - playerPosition.Z
            // Bolts erben die Geschwindigkeit des Schiffs: es zaehlt die Relativbewegung
            let vx = enemy.Velocity.X - player.Velocity.X
            let vy = enemy.Velocity.Y - player.Velocity.Y
            let vz = enemy.Velocity.Z - player.Velocity.Z

            if px * px + py * py + pz * pz < LeadRange * LeadRange then
                let time = Ballistics.interceptTime px py pz vx vy vz Combat.LaserSpeed

                if time > 0.0 then
                    project hud viewProj (ex + vx * time) (ey + vy * time) (ez + vz * time)

                    if onScreen hud then
                        let lx = hud.ScreenX
                        let ly = hud.ScreenY
                        hud.Reserved.Add(HudLayout.around lx ly 11.0)
                        let ax = lx - aimX
                        let ay = ly - aimY
                        // Fadenkreuz auf der Marke = Schussloesung
                        let locked = ax * ax + ay * ay < 13.0 * 13.0
                        stroke ctx (if locked then Green else Amber)
                        ctx.lineWidth <- 1.5
                        ctx.beginPath ()
                        ctx.arc (lx, ly, 7.0, 0.0, 2.0 * Math.PI)

                        if targetVisible then
                            ctx.moveTo (targetX, targetY)
                            ctx.lineTo (lx, ly)

                        ctx.stroke ()
                        fill ctx (if locked then Green else Amber)
                        ctx.fillRect (lx - 1.5, ly - 1.5, 3.0, 3.0)

        // ---- Fadenkreuz und Treffermarke ----
        if aimVisible && player.Alive then
            if opticsActive then
                ctx.save ()
                ctx.font <- FontSmall
                ctx.textAlign <- "left"
                ctx.textBaseline <- "middle"
                fill ctx Cyan
                ctx.fillText ("OPTIK 1,8×", aimX + 29.0, aimY - 13.0)
                ctx.restore ()
            stroke ctx (if player.Overdrive > 0.0 then Amber else Cyan)
            ctx.lineWidth <- 1.5
            ctx.beginPath ()
            ctx.arc (aimX, aimY, 13.0, 0.0, 2.0 * Math.PI)
            if player.Overdrive > 0.0 then
                ctx.arc (aimX, aimY, 19.0, -0.45, 0.45)
                ctx.moveTo (aimX - 19.0 * cos 0.45, aimY - 19.0 * sin 0.45)
                ctx.arc (aimX, aimY, 19.0, Math.PI - 0.45, Math.PI + 0.45)
            ctx.moveTo (aimX - 24.0, aimY)
            ctx.lineTo (aimX - 8.0, aimY)
            ctx.moveTo (aimX + 8.0, aimY)
            ctx.lineTo (aimX + 24.0, aimY)
            ctx.moveTo (aimX, aimY - 24.0)
            ctx.lineTo (aimX, aimY - 8.0)
            ctx.moveTo (aimX, aimY + 8.0)
            ctx.lineTo (aimX, aimY + 24.0)
            ctx.stroke ()

            if cameraMode = Cockpit then
                // Dezente Cockpit-Fluegelmarken links/rechts vom Fadenkreuz
                stroke ctx "rgba(118, 229, 249, 0.34)"
                ctx.lineWidth <- 1.2
                ctx.beginPath ()
                ctx.moveTo (aimX - 64.0, aimY)
                ctx.lineTo (aimX - 46.0, aimY)
                ctx.lineTo (aimX - 46.0, aimY + 5.0)
                ctx.moveTo (aimX + 64.0, aimY)
                ctx.lineTo (aimX + 46.0, aimY)
                ctx.lineTo (aimX + 46.0, aimY + 5.0)
                ctx.stroke ()

            let diagonals (inner: float) (outer: float) =
                ctx.beginPath ()
                ctx.moveTo (aimX - outer, aimY - outer)
                ctx.lineTo (aimX - inner, aimY - inner)
                ctx.moveTo (aimX + outer, aimY - outer)
                ctx.lineTo (aimX + inner, aimY - inner)
                ctx.moveTo (aimX - outer, aimY + outer)
                ctx.lineTo (aimX - inner, aimY + inner)
                ctx.moveTo (aimX + outer, aimY + outer)
                ctx.lineTo (aimX + inner, aimY + inner)
                ctx.stroke ()

            if hud.HitMarker > 0.0 then
                // Kurz nach aussen springend; Schildtreffer blau, Huellentreffer hell-orange
                let k = hud.HitMarker / HitMarkerTime
                ctx.globalAlpha <- Math.Min(1.0, hud.HitMarker * 8.0)
                stroke ctx (if hud.HitShield then "rgb(130, 200, 255)" else "rgb(255, 228, 190)")
                ctx.lineWidth <- 2.0 + k
                diagonals (10.0 + 3.0 * k) (19.0 + 5.0 * k)
                ctx.globalAlpha <- 1.0

            if hud.KillMarker > 0.0 then
                let k = hud.KillMarker / KillMarkerTime
                ctx.globalAlpha <- Math.Min(1.0, hud.KillMarker * 5.0)
                stroke ctx Red
                ctx.lineWidth <- 3.0
                diagonals 12.0 (30.0 + 6.0 * (1.0 - k))
                ctx.globalAlpha <- 0.8 * k
                ctx.lineWidth <- 2.0
                ctx.beginPath ()
                ctx.arc (aimX, aimY, 18.0 + 30.0 * (1.0 - k), 0.0, 2.0 * Math.PI)
                ctx.stroke ()
                ctx.globalAlpha <- 1.0

        // ---- Virtueller Joystick der Maus ----
        if input.PointerLocked then
            stroke ctx CyanDim
            ctx.lineWidth <- 1.0
            ctx.beginPath ()
            ctx.arc (centerX, centerY, 70.0, 0.0, 2.0 * Math.PI)
            ctx.stroke ()
            if hud.HoldStick then
                // Totzone des Modus "Halten": Innerhalb dieses Rings fliegt das Schiff geradeaus.
                ctx.beginPath ()
                ctx.arc (centerX, centerY, 70.0 * 0.06 + 2.0, 0.0, 2.0 * Math.PI)
                ctx.stroke ()
            fill ctx Amber
            ctx.beginPath ()
            ctx.arc (centerX + input.StickX * 70.0, centerY + input.StickY * 70.0, 3.5, 0.0, 2.0 * Math.PI)
            ctx.fill ()

        // ---- Unten links: Schild, Huelle, Tempo, Schub, Boost ----
        let left = 24.0
        let barX = left + 62.0
        let barW = Math.Min(190.0, w - 158.0)
        let baseY = h - 156.0
        let hullFraction = player.Hull / player.MaxHull
        let critical = hullFraction < 0.3
        let scanRange = if school = FlightSchool.Off then RelicArchive.range world.Pilot else 0.0
        fill ctx "rgba(3, 12, 23, 0.72)"
        ctx.fillRect (left - 12.0, baseY - 18.0, barW + 118.0, 164.0)
        hud.Reserved.Add(HudLayout.rect (left-12.0) (baseY-18.0) (barW+118.0) 164.0)
        fill ctx Cyan
        ctx.fillRect (left - 12.0, baseY - 18.0, 34.0, 2.0)
        stroke ctx CyanDim
        ctx.strokeRect (left - 12.0, baseY - 18.0, barW + 118.0, 164.0)
        ctx.textAlign <- "left"
        ctx.textBaseline <- "top"
        ctx.font <- FontLabel
        if w < 760.0 && scanRange > 0.0 && player.Alive then
            fill ctx "#deabf3"
            let labelWidth = if w < 680.0 then max 64.0 (w - 242.0) else 200.0
            ctx.fillText ("[G] Scan " + number hud scanRange + " m", left, baseY - 15.0, labelWidth)
        fill ctx TextColor
        ctx.fillText ("SCHILD", left, baseY)
        ctx.fillText ("HÜLLE", left, baseY + 22.0)
        ctx.fillText ("TEMPO", left, baseY + 54.0)
        ctx.fillText ("SCHUB", left, baseY + 84.0)
        ctx.fillText ("BOOST", left, baseY + 102.0)
        bar ctx barX (baseY + 1.0) barW 10.0 (player.Shield / player.MaxShield) ShieldBlue
        bar ctx barX (baseY + 23.0) barW 10.0 hullFraction (if critical then Red else Green)
        bar ctx barX (baseY + 86.0) barW 6.0 player.Controls.Throttle (if player.Controls.Brake then Red else Cyan)
        bar ctx barX (baseY + 104.0) barW 6.0 hud.BoostReserve (if hud.BoostLocked then Red else Amber)
        ctx.font <- FontSmall
        fill ctx TextColor
        ctx.fillText (number hud (player.Shield / player.MaxShield * 100.0), barX + barW + 8.0, baseY)
        ctx.fillText (number hud (hullFraction * 100.0), barX + barW + 8.0, baseY + 22.0)
        ctx.font <- FontValue
        fill ctx Cyan
        ctx.fillText (number hud (Vec3.length player.Velocity) + " m/s", barX, baseY + 46.0)
        if hud.MatchSpeed then
            ctx.font <- FontSmall
            fill ctx Amber
            ctx.textAlign <- "right"
            ctx.fillText ("= ZIELTEMPO", barX + barW + 30.0, baseY + 56.0)
            ctx.textAlign <- "left"
        let keyWidth = (barW + 88.0) / 3.0
        let key index label action =
            let x = left + float index * (keyWidth + 3.0)
            let pressed = Input.isDown input action
            fill ctx (if pressed then "#87e5d7" else "#1d3642")
            ctx.fillRect (x, baseY+118.0, keyWidth, 21.0)
            ctx.font <- FontLabel
            fill ctx (if pressed then "#08232b" else "#d9f4ee")
            ctx.fillText (label, x+5.0, baseY+122.0, keyWidth-8.0)
        key 0 "↑ Schub +" GameAction.ThrottleUp
        key 1 "↓ Schub −" GameAction.ThrottleDown
        key 2 "X Bremse" GameAction.Brake

        // Blinkende Warnung bei kritischer Huelle oder aktivem Overdrive
        if critical && player.Alive && sin (hud.Time * 9.0) > 0.0 then
            ctx.font <- FontLabel
            fill ctx Red
            ctx.fillText ("HUELLE KRITISCH", left, baseY - 38.0)
        elif player.Overdrive > 0.0 && player.Alive then
            ctx.font <- FontLabel
            fill ctx Amber
            ctx.fillText ("WAFFEN-OVERDRIVE AKTIV", left, baseY - 38.0)

        // ---- Unten Mitte: 3D-Sensorschirm (Gravidar) ----
        if w >= 760.0 && player.Alive then
            drawRadar hud world sector school sortie scanElapsed scanRange alpha playerPosition playerRotation target
            hud.Reserved.Add(HudLayout.rect (centerX-92.0) (h-118.0) 184.0 102.0)

        // ---- Unten rechts: Welle, Gegner, Punkte + Kill-Kombo ----
        if score <> hud.ScoreValue then
            hud.ScoreValue <- score
            hud.ScoreText <- string score

        let right = w - 24.0
        let scoreBottom = if w < 680.0 then h - 156.0 else h

        if not sortie && school = FlightSchool.Off then
            let comboActive = hud.Combo > 1 && hud.ComboTimer > 0.0
            if comboActive then
                fill ctx "rgba(3, 12, 23, 0.78)"
                ctx.fillRect (right - 190.0, scoreBottom - 166.0, 202.0, 34.0)
                hud.Reserved.Add(HudLayout.rect (right-190.0) (scoreBottom-166.0) 202.0 34.0)
                stroke ctx Amber
                ctx.strokeRect (right - 190.0, scoreBottom - 166.0, 202.0, 34.0)
                ctx.textAlign <- "left"
                ctx.font <- FontLabel
                fill ctx Amber
                ctx.fillText ("KOMBO x" + hud.Numbers.[hud.Combo], right - 178.0, scoreBottom - 158.0)
                bar ctx (right - 96.0) (scoreBottom - 154.0) 96.0 6.0 (hud.ComboTimer / 8.0) Amber

            let missileY = if comboActive then scoreBottom - 204.0 else scoreBottom - 166.0
            let lockReady =
                target >= 0 && enemies.[target].Alive && hud.MissileAmmo + hud.MissileReserve > 0 &&
                Vec3.distance playerPosition enemies.[target].Position <= Missiles.Range
            fill ctx "rgba(3, 12, 23, 0.74)"
            ctx.fillRect (right - 190.0, missileY, 202.0, 32.0)
            hud.Reserved.Add(HudLayout.rect (right-190.0) missileY 202.0 32.0)
            stroke ctx (if lockReady then Amber else CyanDim)
            ctx.strokeRect (right - 190.0, missileY, 202.0, 32.0)
            ctx.textAlign <- "left"
            ctx.font <- FontLabel
            fill ctx (if lockReady then Amber else TextColor)
            let missileStatus = if lockReady then "RAKETE [J] · BEREIT" else "RAKETEN [J]"
            let reserve = if hud.MissileReserve > 0 then "  +" + string hud.MissileReserve + " RES." else ""
            ctx.fillText (missileStatus + reserve, right - 178.0, missileY + 6.0, 184.0)
            let cap = max 1 hud.MissileCapacity
            let step = min 28.0 (118.0 / float cap)
            let pipW = max 7.0 (step - 5.0)
            for p in 0 .. cap - 1 do
                let px = right - 178.0 + float p * step
                let filled = p < hud.MissileAmmo
                fill ctx (if filled then (if lockReady then Amber else Cyan) else "rgba(120, 175, 195, 0.18)")
                ctx.fillRect (px, missileY + 21.0, pipW, 5.0)
            if hud.MissileAmmo < cap then
                bar ctx (right - 54.0) (missileY + 21.0) 54.0 5.0 hud.MissileReload CyanDim

            fill ctx "rgba(3, 12, 23, 0.72)"
            ctx.fillRect (right - 190.0, scoreBottom - 126.0, 202.0, 116.0)
            hud.Reserved.Add(HudLayout.rect (right-190.0) (scoreBottom-126.0) 202.0 116.0)
            stroke ctx CyanDim
            ctx.strokeRect (right - 190.0, scoreBottom - 126.0, 202.0, 116.0)
            fill ctx Amber
            ctx.fillRect (right - 22.0, scoreBottom - 126.0, 34.0, 2.0)
            ctx.textAlign <- "right"
            ctx.font <- FontLabel
            fill ctx TextColor
            ctx.fillText ((if hud.Trading then "REISEN" else "SEKTOR"), right - 78.0, scoreBottom - 98.0)
            ctx.fillText ("GEGNER", right - 78.0, scoreBottom - 68.0)
            ctx.fillText ((if hud.Trading then "GEWINN" else "PUNKTE"), right - 78.0, scoreBottom - 38.0)
            ctx.font <- FontValue
            fill ctx Cyan
            ctx.fillText ((if hud.Trading then string hud.Trips else number hud (float (max 1 wave)) + "/6"), right, scoreBottom - 105.0)
            fill ctx (if enemiesAlive > 0 then Red else TextDim)
            ctx.fillText (number hud (float (enemiesAlive + hud.PendingEnemies)), right, scoreBottom - 75.0)
            fill ctx Amber
            ctx.fillText ((if hud.Trading then string hud.TradeProfit else hud.ScoreText), right, scoreBottom - 45.0)

        // Auftrag bleibt sichtbar, auch wenn die kurze Sektormeldung verschwunden ist.
        if school = FlightSchool.Off then
            let missionWidth = if w < 680.0 then w-32.0 else min 560.0 (w-268.0)
            fill ctx "rgba(3, 12, 23, 0.78)"
            ctx.fillRect (16.0, 38.0, missionWidth, 80.0)
            hud.Reserved.Add(HudLayout.rect 16.0 38.0 missionWidth 80.0)
            ctx.textAlign <- "left"
            ctx.font <- FontLabel
            fill ctx Cyan
            ctx.fillText ((if sortie then "KEPLER-FLUGDIENST  /  " elif hud.Trading then "HANDELSFLUG  /  " else "MORGENLICHT  /  ") + hud.StageName, 28.0, 49.0, missionWidth - 24.0)
            ctx.font <- FontSmall
            fill ctx TextColor
            ctx.fillText (hud.Objective, 28.0, 69.0, missionWidth - 24.0)
            fill ctx TextDim
            let status =
                if sortie then "Abfangjäger im Feld · " + string enemiesAlive
                elif hud.Trading && hud.Salvaging then "↑ Schub · X Stopp · B Dock · Handelsziel: " + string hud.TradeProfit + " / 1.000 CR"
                elif hud.Salvaging then "BERGUNG · Station anfliegen · [B] am Dock"
                elif hud.Countdown > 0 then "Feindlicher Anflug in " + string hud.Countdown + " s"
                else string enemiesAlive + " im Feld · " + string hud.PendingEnemies + " Verstärkungen unterwegs"
            let pulse =
                if PilotSkills.level world.Pilot 0 < 3 then ""
                elif world.Pilot.PulseCooldown <= 0.0 then " · V Störimpuls bereit"
                else " · V in " + string (int (ceil world.Pilot.PulseCooldown)) + " s"
            ctx.fillText (status + pulse, 28.0, 88.0, missionWidth - 24.0)
            bar ctx 28.0 109.0 (missionWidth - 24.0) 3.0 hud.StageProgress Cyan

        // Geld ist Kaufkraft, Punkte bleiben die getrennte Flugwertung.
        let compactTarget = target >= 0 && (w < 680.0 || h < 500.0)
        if school <> FlightSchool.Off && w >= 680.0 && not compactTarget then
            fill ctx "rgba(3, 12, 23, 0.85)"
            ctx.fillRect (w-236.0,42.0,220.0,62.0)
            hud.Reserved.Add(HudLayout.rect (w-236.0) 42.0 220.0 62.0)
            ctx.textAlign <- "right"
            ctx.font <- FontValue
            fill ctx Amber
            ctx.fillText (string hud.Credits + " CR",w-28.0,49.0)
            ctx.font <- FontSmall
            fill ctx TextColor
            ctx.fillText ("Je Übungsziel einmalig 100 CR",w-28.0,80.0,200.0)
        elif not sortie && school = FlightSchool.Off && not compactTarget then
            let walletY = if w < 680.0 then 128.0 else 42.0
            fill ctx "rgba(3, 12, 23, 0.85)"
            ctx.fillRect (w - 236.0, walletY, 220.0, 113.0)
            hud.Reserved.Add(HudLayout.rect (w-236.0) walletY 220.0 113.0)
            ctx.textAlign <- "right"
            ctx.font <- FontValue
            fill ctx Amber
            ctx.fillText (string hud.Credits + " CR", w-28.0, walletY+8.0)
            ctx.font <- FontSmall
            fill ctx (if hud.TurretOwned then Cyan else TextDim)
            let turret = if not hud.TurretOwned then "AUTO-TURRET · WERFT: 350 CR" elif hud.TurretTarget >= 0 then "AUTO-TURRET · ZIEL ERFASST" else "AUTO-TURRET · SUCHT ZIEL"
            ctx.fillText (turret, w-28.0, walletY+43.0)
            fill ctx (if hud.CargoUsed >= hud.CargoCapacity then Amber else Cyan)
            ctx.fillText ("FRACHT " + string hud.CargoUsed + "/" + string hud.CargoCapacity + " t" + (if hud.CargoUsed >= hud.CargoCapacity then " · VOLL → STATION" else " · KRYO-ERZ BERGEN"),w-28.0,walletY+67.0,200.0)
            fill ctx (if hud.SaveFailed then Amber else TextDim)
            let saveStatus = if hud.SaveFailed then "Speicherhinweis · Details mit F8" else hud.SaveText
            ctx.fillText (saveStatus, w - 28.0, walletY + 90.0, 200.0)
        elif hud.SaveFailed then
            ctx.textAlign <- "right"
            fill ctx Amber
            ctx.fillText ("Speicherhinweis · Details mit F8", w-16.0, 21.0)
        if hud.RewardTime > 0.0 then
            ctx.textAlign <- "center"
            fill ctx Amber
            ctx.font <- FontLabel
            ctx.fillText (hud.RewardText, centerX, h-185.0, w-32.0)
            let width = min (w-32.0) (ctx.measureText(hud.RewardText).width)
            hud.Reserved.Add(HudLayout.rect (centerX-width*0.5) (h-188.0) width 20.0)

        // ---- Meldung (blendet am Ende aus) ----
        if hud.BannerTime > 0.0 then
            let fadeOut = Math.Min(1.0, hud.BannerTime / 0.6)
            let fadeIn = Math.Min(1.0, (hud.BannerDuration - hud.BannerTime) / 0.25)
            ctx.globalAlpha <- fadeOut * fadeIn
            ctx.textAlign <- "center"
            ctx.font <- FontBanner
            fill ctx Cyan
            let bannerY = max 145.0 (h * 0.2)
            ctx.fillText (hud.Banner, centerX, bannerY, w - 32.0)
            let titleWidth = ctx.measureText(hud.Banner).width
            ctx.font <- FontSmall
            fill ctx TextColor
            ctx.fillText (hud.BannerSub, centerX, bannerY + 40.0, w - 32.0)
            let width = min (w-32.0) (max titleWidth (ctx.measureText(hud.BannerSub).width))
            hud.Reserved.Add(HudLayout.rect (centerX-width*0.5) bannerY width 56.0)
            ctx.globalAlpha <- 1.0

        // ---- Fraktions-Funk-Ticker (unten mittig) ----
        if hud.RadioTime > 0.0 && hud.RadioText <> "" then
            let fadeOut = Math.Min(1.0, hud.RadioTime / 0.45)
            let fadeIn = Math.Min(1.0, (hud.RadioDuration - hud.RadioTime) / 0.20)
            let accent =
                match hud.RadioFactionTag with
                | 0 -> Amber
                | 2 -> Red
                | 3 -> "#9be7d8"
                | _ -> Cyan
            let panHint =
                if hud.RadioPan < -0.2 then "◀ "
                elif hud.RadioPan > 0.2 then "▶ "
                else "· "
            let header = panHint + "FUNK // " + hud.RadioCallsign
            let boxW = min 540.0 (max 280.0 (w - 40.0))
            let boxX = centerX - boxW * 0.5
            let boxY = if w < 860.0 then h - 222.0 else h - 122.0
            ctx.globalAlpha <- fadeOut * fadeIn
            fill ctx "rgba(3, 12, 23, 0.84)"
            ctx.fillRect (boxX, boxY, boxW, 46.0)
            fill ctx accent
            ctx.fillRect (boxX, boxY, 3.0, 46.0)
            stroke ctx CyanDim
            ctx.lineWidth <- 1.0
            ctx.strokeRect (boxX, boxY, boxW, 46.0)
            ctx.textAlign <- "left"
            ctx.textBaseline <- "top"
            ctx.font <- FontSmall
            fill ctx accent
            ctx.fillText (header, boxX + 12.0, boxY + 6.0, boxW - 24.0)
            fill ctx TextColor
            ctx.fillText (hud.RadioText, boxX + 12.0, boxY + 24.0, boxW - 24.0)
            hud.Reserved.Add(HudLayout.rect boxX boxY boxW 46.0)
            ctx.globalAlpha <- 1.0

        // ---- Statistik oben links & Systemstatus oben rechts ----
        ctx.textAlign <- "left"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText (hud.StatsText, 16.0, 12.0)

        ctx.textAlign <- "right"
        let camLabel = if cameraMode = Cockpit then "COCKPIT" else "VERFOLGER"
        let soundLabel = if hud.Muted then "TON AUS" else "TON AN"
        ctx.fillText ("[F1] HILFE   [C] " + camLabel + "   [T] ZIEL   [M] " + soundLabel, right, 12.0)

        hud.Reserved.Add(HudLayout.rect 0.0 0.0 w 32.0)
        if scanElapsed >= 0.0 then
            hud.Reserved.Add(HudLayout.rect 16.0 (if w < 680.0 then 123.0 else max 129.0 (0.31*h)) 300.0 118.0)
        if target >= 0 && player.Alive then
            let enemy = enemies.[target]
            let delta = Vec3.lerp enemy.PrevPosition enemy.Position alpha - playerPosition
            let distance = Vec3.length delta
            let closing = Vec3.dot (player.Velocity-enemy.Velocity) delta / max 0.001 distance
            let box = HudLayout.targetBox w h (not compactTarget && not sortie) sightBounds hud.Reserved
            hud.Reserved.Add box
            HudContacts.targetPanel ctx box enemy brains.[target] distance closing (FlightSchool.onRange school)

    /// Abgedunkelter Hintergrund fuer die Vollbild-Texte (Menue, Pause, Game Over).
    let private dim (hud: Hud) (opacity: float) =
        let ctx = hud.Ctx
        ctx.globalAlpha <- opacity
        fill ctx "rgb(2, 3, 10)"
        ctx.fillRect (0.0, 0.0, hud.Width, hud.Height)
        ctx.globalAlpha <- 1.0

    /// Blinkende Aufforderung am unteren Rand des Textblocks.
    let private prompt (hud: Hud) (text: string) (y: float) =
        let ctx = hud.Ctx
        ctx.globalAlpha <- 0.7 + 0.3 * sin (hud.Time * 4.0)
        ctx.font <- FontLabel
        fill ctx Cyan
        ctx.fillText (text, hud.Width * 0.5, y)
        ctx.globalAlpha <- 1.0

    /// Pausen-Overlay (ueber dem weiter sichtbaren Flug-HUD).
    let drawPaused (hud: Hud) (mouseSensitivity: int) =
        let ctx = hud.Ctx
        dim hud 0.5
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontTitle
        fill ctx Cyan
        ctx.fillText ("PAUSE", hud.Width * 0.5, hud.Height * 0.36)
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText ("Maus " + string mouseSensitivity + " %   ·   Komma langsamer   ·   Punkt schneller", hud.Width * 0.5, hud.Height * 0.36 + 60.0, hud.Width - 28.0)
        ctx.fillText ("Mausmodus: " + (if hud.HoldStick then "HALTEN (Stick bleibt stehen)" else "WISCHEN (Stick federt zurück)") + "   ·   U wechselt", hud.Width * 0.5, hud.Height * 0.36 + 84.0, hud.Width - 28.0)
        prompt hud "P / ENTER / KLICK zum Fortsetzen · F8 für Spielstände" (hud.Height * 0.36 + 116.0)

    let private helpRows =
        [| "W / S · A / D", "Nase senken/heben · gieren"
           "Q / E", "Rollen"
           "Maus", "Steuern (Klick fängt die Maus, Esc gibt frei)"
           "U", "Mausmodus Wischen / Halten"
           "Mitteltaste", "Frei umsehen (halten)"
           "↑ / ↓ · Mausrad", "Schub regeln"
           "→ · ←", "Vollschub · Nullschub"
           "X", "Bremsen bis zum Stillstand"
           "Shift", "Boost (Vorrat im BOOST-Balken)"
           "L", "Tempo ans erfasste Ziel angleichen"
           "Leertaste · LMB", "Feuern"
           "T / Tab · RMB", "Ziel wechseln"
           "N", "Nächstes Ziel (erneut: nächstfernes)"
           "V · J", "Störimpuls · Torpedo (Bomber)"
           "B · G", "Andocken · Archivscan / Wrack bergen"
           "C · H", "Kamera wechseln · Heckblick (halten)"
           "Bild ↑ / ↓", "Verfolgerkamera näher / weiter"
           "O", "Flugoptik 1,8× und Feinsteuerung"
           "Komma · Punkt", "Maus-Empfindlichkeit − / +"
           "I · M", "Pitch invertieren · Ton"
           "P · F8", "Pause · Spielstände"
           "F3 · F4", "Bildrate · Schatten an/aus"
           "F7", "Universumskarte im Handelsflug" |]

    /// Tastenübersicht über dem Flugbild (F1). Der Flug läuft darunter weiter.
    let drawHelp (hud: Hud) =
        let ctx = hud.Ctx
        let columns = if hud.Width >= 860.0 then 2 else 1
        let perColumn = (helpRows.Length + columns - 1) / columns
        let rowHeight = 23.0
        let width = Math.Min(hud.Width - 32.0, (if columns = 2 then 820.0 else 430.0))
        let height = 62.0 + float perColumn * rowHeight
        let x0 = (hud.Width - width) * 0.5
        let y0 = Math.Max(16.0, (hud.Height - height) * 0.5)
        let columnWidth = width / float columns
        fill ctx "rgba(3, 12, 23, 0.94)"
        ctx.fillRect (x0, y0, width, height)
        stroke ctx CyanDim
        ctx.lineWidth <- 1.0
        ctx.strokeRect (x0, y0, width, height)
        fill ctx Cyan
        ctx.fillRect (x0, y0, 34.0, 2.0)
        ctx.textBaseline <- "top"
        ctx.textAlign <- "left"
        ctx.font <- FontLabel
        ctx.fillText ("FLUGSTEUERUNG", x0 + 18.0, y0 + 16.0)
        ctx.textAlign <- "right"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText ("F1 schließt", x0 + width - 18.0, y0 + 18.0)
        ctx.textAlign <- "left"

        for i in 0 .. helpRows.Length - 1 do
            let key, text = helpRows.[i]
            let x = x0 + 18.0 + float (i / perColumn) * columnWidth
            let y = y0 + 48.0 + float (i % perColumn) * rowHeight
            fill ctx Amber
            ctx.fillText (key, x, y, 128.0)
            fill ctx TextColor
            ctx.fillText (text, x + 136.0, y, columnWidth - 160.0)

    /// Game-Over-Bildschirm mit dem per setResult hinterlegten Ergebnis.
    let drawGameOver (hud: Hud) =
        let ctx = hud.Ctx
        let cx = hud.Width * 0.5
        let top = hud.Height * 0.26
        dim hud 0.55
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontTitle
        fill ctx (if hud.ResultVictory then Cyan else Red)
        ctx.fillText ((if hud.ResultVictory then "ROUTE GESICHERT" else "SCHIFF ZERSTÖRT"), cx, top, hud.Width - 32.0)
        if hud.ResultVictory then
            ctx.font <- FontSmall
            fill ctx TextColor
            ctx.fillText ("Die Blockade ist gefallen. Operation Morgenlicht abgeschlossen.", cx, top + 54.0, hud.Width - 32.0)
        ctx.font <- FontValue
        fill ctx Amber
        ctx.fillText (hud.ResultScoreText, cx, top + 78.0)
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText (hud.ResultDetailText, cx, top + 118.0)
        fill ctx (if hud.ResultIsBest then Green else TextDim)
        ctx.fillText (hud.ResultBestText, cx, top + 144.0)
        let action =
            if hud.ResultVictory then "ENTER / Klick · Neuen Flug wählen"
            elif hud.HasSave then "ENTER / Klick · Letzten Spielstand laden"
            else "ENTER / Klick · Neuer Versuch"
        prompt hud (action + "     F8 · Spielstände") (top + 196.0)
