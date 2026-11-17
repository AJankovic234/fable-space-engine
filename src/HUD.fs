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
      mutable ResultScoreText: string
      mutable ResultDetailText: string
      mutable ResultBestText: string
      mutable ResultIsBest: bool
      // --- Meldungen und Effekte ---
      mutable Banner: string
      mutable BannerSub: string
      mutable BannerTime: float
      mutable BannerDuration: float
      /// > 0: Treffermarke am Fadenkreuz sichtbar (eigener Laser hat getroffen).
      mutable HitMarker: float
      /// 0..1: roter Bildrand nach erlittenem Schaden.
      mutable DamageFlash: float
      /// Index des erfassten Gegners oder -1.
      mutable Target: int
      mutable Time: float }

[<RequireQualifiedAccess>]
module Hud =

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
    let private RedDim = "rgba(255, 90, 70, 0.3)"

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
          ResultScoreText = ""
          ResultDetailText = ""
          ResultBestText = ""
          ResultIsBest = false
          Banner = ""
          BannerSub = ""
          BannerTime = 0.0
          BannerDuration = 1.0
          HitMarker = 0.0
          DamageFlash = 0.0
          Target = -1
          Time = 0.0 }

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

    /// Eigener Laser hat ein Schiff getroffen.
    let hitMarker (hud: Hud) = hud.HitMarker <- 0.2

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
        hud.ResultDetailText <- "Welle " + string wave + "   ::   " + string kills + " Abschuesse"
        hud.ResultBestText <- (if isBest then "Neuer Bestwert!" else "Bestwert  " + string best)
        hud.ResultIsBest <- isBest

    /// Laesst Meldungen und Effekte abklingen (einmal pro Frame).
    let update (hud: Hud) (dt: float) =
        hud.Time <- hud.Time + dt
        hud.BannerTime <- Math.Max(0.0, hud.BannerTime - dt)
        hud.HitMarker <- Math.Max(0.0, hud.HitMarker - dt)
        hud.DamageFlash <- Math.Max(0.0, hud.DamageFlash - dt * 1.6)

    /// Loescht das Overlay und stellt die Zeichen-Transformation ein.
    /// Erster Aufruf jedes Frames, vor allen draw*-Funktionen.
    let clear (hud: Hud) =
        let ctx = hud.Ctx
        ctx.setTransform (hud.Ratio, 0.0, 0.0, hud.Ratio, 0.0, 0.0)
        ctx.clearRect (0.0, 0.0, hud.Width, hud.Height)

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

    /// Das Flug-HUD.
    ///   viewProj/aspect : Kamera des aktuellen Frames
    ///   alpha           : Interpolationsfaktor zwischen den letzten Physikzustaenden
    ///   playerPosition/-Rotation : interpolierte Darstellungspose des Spielers
    let drawFlight
        (hud: Hud)
        (viewProj: Mat4)
        (aspect: float)
        (world: World)
        (brains: AiBrain[])
        (input: InputState)
        (alpha: float)
        (playerPosition: Vec3)
        (playerRotation: Quat)
        (wave: int)
        (score: int)
        (enemiesAlive: int)
        =
        let ctx = hud.Ctx
        let w = hud.Width
        let h = hud.Height
        let player = world.Player
        let enemies = world.Enemies
        let centerX = w * 0.5
        let centerY = h * 0.5

        // ---- Treffer-Feedback: roter Bildrand ----
        if hud.DamageFlash > 0.01 then
            ctx.globalAlpha <- hud.DamageFlash
            ctx.fillStyle <- U3.Case2 hud.Vignette
            ctx.fillRect (0.0, 0.0, w, h)
            ctx.globalAlpha <- 1.0

        // ---- Zielpunkt des Schiffs (Fadenkreuz) ----
        let forward = Quat.forward playerRotation
        project hud viewProj (playerPosition.X + forward.X * 800.0) (playerPosition.Y + forward.Y * 800.0) (playerPosition.Z + forward.Z * 800.0)
        let aimVisible = hud.ClipW > 1e-3
        let aimX = if aimVisible then hud.ScreenX else centerX
        let aimY = if aimVisible then hud.ScreenY else centerY

        // ---- Zielerfassung: der Gegner, der dem Fadenkreuz am naechsten liegt;
        //      ist keiner im Bild, der raeumlich naechste. Das aktuelle Ziel bekommt
        //      einen Bonus, damit die Erfassung nicht zwischen zwei Gegnern flattert.
        let mutable target = -1
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

        // ---- Gegner-Marker: Rahmen im Bild, Pfeile am Rand ----
        ctx.textAlign <- "center"
        ctx.textBaseline <- "middle"
        ctx.font <- FontSmall
        let mutable targetX = 0.0
        let mutable targetY = 0.0
        let mutable targetVisible = false

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
                let isTarget = i = target

                let attacking =
                    match brains.[i].State with
                    | AiState.Attack -> true
                    | _ -> false

                if onScreen hud then
                    let x = hud.ScreenX
                    let y = hud.ScreenY
                    // Rahmengroesse folgt der scheinbaren Groesse, mit Mindestmass
                    let size = Math.Max((if isTarget then 16.0 else 9.0), enemy.Radius * 1.1 * h / hud.ClipW)
                    ctx.lineWidth <- (if isTarget then 2.0 else 1.2)
                    stroke ctx (if isTarget then Red else RedDim)
                    bracket ctx x y size (size * 0.4)

                    if isTarget then
                        targetX <- x
                        targetY <- y
                        targetVisible <- true
                        // Zustandsbalken (Huelle + Schild) und Entfernung
                        let health = (enemy.Hull + enemy.Shield) / (enemy.MaxHull + enemy.MaxShield)
                        fill ctx RedDim
                        ctx.fillRect (x - size, y - size - 8.0, size * 2.0, 3.0)
                        fill ctx Red
                        ctx.fillRect (x - size, y - size - 8.0, size * 2.0 * health, 3.0)
                        ctx.fillText (number hud distance, x, y + size + 11.0)
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
                    let limitY = centerY - EdgeMargin

                    let scale =
                        Math.Min(
                            (if abs sx > 1e-6 then limitX / abs sx else Double.MaxValue),
                            (if abs sy > 1e-6 then limitY / abs sy else Double.MaxValue)
                        )

                    let x = centerX + sx * scale
                    let y = centerY + sy * scale
                    // Angreifer pulsieren, nahe Gegner bekommen groessere Pfeile
                    let pulse = if attacking then 0.75 + 0.25 * sin (hud.Time * 12.0) else 1.0
                    let size = (if distance < 250.0 then 13.0 else 9.5) * pulse
                    fill ctx (if attacking || isTarget then Red else RedSoft)
                    arrow ctx x y sx sy size
                    fill ctx TextColor
                    ctx.fillText (number hud distance, x - sx * 26.0, y - sy * 26.0)

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
            stroke ctx Cyan
            ctx.lineWidth <- 1.5
            ctx.beginPath ()
            ctx.arc (aimX, aimY, 13.0, 0.0, 2.0 * Math.PI)
            ctx.moveTo (aimX - 24.0, aimY)
            ctx.lineTo (aimX - 8.0, aimY)
            ctx.moveTo (aimX + 8.0, aimY)
            ctx.lineTo (aimX + 24.0, aimY)
            ctx.moveTo (aimX, aimY - 24.0)
            ctx.lineTo (aimX, aimY - 8.0)
            ctx.moveTo (aimX, aimY + 8.0)
            ctx.lineTo (aimX, aimY + 24.0)
            ctx.stroke ()

            if hud.HitMarker > 0.0 then
                ctx.globalAlpha <- Math.Min(1.0, hud.HitMarker * 8.0)
                stroke ctx "rgb(255, 255, 255)"
                ctx.lineWidth <- 2.0
                ctx.beginPath ()
                ctx.moveTo (aimX - 20.0, aimY - 20.0)
                ctx.lineTo (aimX - 11.0, aimY - 11.0)
                ctx.moveTo (aimX + 20.0, aimY - 20.0)
                ctx.lineTo (aimX + 11.0, aimY - 11.0)
                ctx.moveTo (aimX - 20.0, aimY + 20.0)
                ctx.lineTo (aimX - 11.0, aimY + 11.0)
                ctx.moveTo (aimX + 20.0, aimY + 20.0)
                ctx.lineTo (aimX + 11.0, aimY + 11.0)
                ctx.stroke ()
                ctx.globalAlpha <- 1.0

        // ---- Virtueller Joystick der Maus ----
        if input.PointerLocked then
            stroke ctx CyanDim
            ctx.lineWidth <- 1.0
            ctx.beginPath ()
            ctx.arc (centerX, centerY, 70.0, 0.0, 2.0 * Math.PI)
            ctx.stroke ()
            fill ctx Amber
            ctx.beginPath ()
            ctx.arc (centerX + input.StickX * 70.0, centerY + input.StickY * 70.0, 3.5, 0.0, 2.0 * Math.PI)
            ctx.fill ()

        // ---- Unten links: Schild, Huelle, Tempo, Schub, Boost ----
        let left = 24.0
        let barX = left + 62.0
        let barW = 190.0
        let baseY = h - 132.0
        let hullFraction = player.Hull / player.MaxHull
        let critical = hullFraction < 0.3
        ctx.textAlign <- "left"
        ctx.textBaseline <- "top"
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText ("SCHILD", left, baseY)
        ctx.fillText ("HUELLE", left, baseY + 22.0)
        ctx.fillText ("TEMPO", left, baseY + 54.0)
        ctx.fillText ("SCHUB", left, baseY + 84.0)
        ctx.fillText ("BOOST", left, baseY + 102.0)
        bar ctx barX (baseY + 1.0) barW 10.0 (player.Shield / player.MaxShield) ShieldBlue
        bar ctx barX (baseY + 23.0) barW 10.0 hullFraction (if critical then Red else Green)
        bar ctx barX (baseY + 86.0) barW 6.0 player.Controls.Throttle (if player.Controls.Brake then Red else Cyan)
        bar ctx barX (baseY + 104.0) barW 6.0 player.Boost Amber
        ctx.font <- FontSmall
        fill ctx TextColor
        ctx.fillText (number hud (player.Shield / player.MaxShield * 100.0), barX + barW + 8.0, baseY)
        ctx.fillText (number hud (hullFraction * 100.0), barX + barW + 8.0, baseY + 22.0)
        ctx.font <- FontValue
        fill ctx Cyan
        ctx.fillText (number hud (Vec3.length player.Velocity), barX, baseY + 46.0)

        // Blinkende Warnung bei kritischer Huelle
        if critical && player.Alive && sin (hud.Time * 9.0) > 0.0 then
            ctx.font <- FontLabel
            fill ctx Red
            ctx.fillText ("HUELLE KRITISCH", left, baseY - 24.0)

        // ---- Unten rechts: Welle, Gegner, Punkte ----
        if score <> hud.ScoreValue then
            hud.ScoreValue <- score
            hud.ScoreText <- string score

        let right = w - 24.0
        ctx.textAlign <- "right"
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText ("WELLE", right - 78.0, h - 98.0)
        ctx.fillText ("GEGNER", right - 78.0, h - 68.0)
        ctx.fillText ("PUNKTE", right - 78.0, h - 38.0)
        ctx.font <- FontValue
        fill ctx Cyan
        ctx.fillText (number hud (float wave), right, h - 105.0)
        fill ctx (if enemiesAlive > 0 then Red else TextDim)
        ctx.fillText (number hud (float enemiesAlive), right, h - 75.0)
        fill ctx Amber
        ctx.fillText (hud.ScoreText, right, h - 45.0)

        // ---- Meldung (blendet am Ende aus) ----
        if hud.BannerTime > 0.0 then
            let fadeOut = Math.Min(1.0, hud.BannerTime / 0.6)
            let fadeIn = Math.Min(1.0, (hud.BannerDuration - hud.BannerTime) / 0.25)
            ctx.globalAlpha <- fadeOut * fadeIn
            ctx.textAlign <- "center"
            ctx.font <- FontBanner
            fill ctx Cyan
            ctx.fillText (hud.Banner, centerX, h * 0.2)
            ctx.font <- FontSmall
            fill ctx TextColor
            ctx.fillText (hud.BannerSub, centerX, h * 0.2 + 40.0)
            ctx.globalAlpha <- 1.0

        // ---- Statistik oben links (dezent) ----
        ctx.textAlign <- "left"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText (hud.StatsText, 16.0, 12.0)

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

    /// Eine Zeile der Steuerungstabelle: Taste rechtsbuendig, Wirkung linksbuendig.
    let private menuRow (ctx: CanvasRenderingContext2D) (cx: float) (y: float) (key: string) (action: string) =
        ctx.textAlign <- "right"
        fill ctx Amber
        ctx.fillText (key, cx - 14.0, y)
        ctx.textAlign <- "left"
        fill ctx TextColor
        ctx.fillText (action, cx + 14.0, y)

    /// Titelbildschirm mit Steuerungsuebersicht.
    let drawMenu (hud: Hud) =
        let ctx = hud.Ctx
        let cx = hud.Width * 0.5
        let top = hud.Height * 0.2
        dim hud 0.45
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontTitle
        fill ctx Cyan
        ctx.fillText ("FABLE SPACE ENGINE", cx, top)
        ctx.font <- FontSmall
        fill ctx TextColor
        ctx.fillText ("3D-Weltraum-Prototyp  ::  F# + natives WebGL2", cx, top + 62.0)

        // Steuerung als zweispaltige Tabelle
        let rowY = top + 118.0
        ctx.font <- FontLabel
        menuRow ctx cx rowY "W / S" "Nase runter / hoch"
        menuRow ctx cx (rowY + 22.0) "A / D" "Rolle links / rechts"
        menuRow ctx cx (rowY + 44.0) "Q / E" "Gieren links / rechts"
        menuRow ctx cx (rowY + 66.0) "Shift" "Boost"
        menuRow ctx cx (rowY + 88.0) "Pfeil hoch / runter" "Schubhebel"
        menuRow ctx cx (rowY + 110.0) "X" "Bremse"
        menuRow ctx cx (rowY + 132.0) "Leertaste / linke Maustaste" "Feuer"
        menuRow ctx cx (rowY + 154.0) "Maus (nach Klick ins Bild)" "Pitch und Yaw"
        menuRow ctx cx (rowY + 176.0) "C" "Kamera wechseln"
        menuRow ctx cx (rowY + 198.0) "P" "Pause"

        ctx.textAlign <- "center"
        prompt hud "ENTER oder Klick zum Starten" (rowY + 252.0)
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText (hud.BestText, cx, rowY + 282.0)

    /// Pausen-Overlay (ueber dem weiter sichtbaren Flug-HUD).
    let drawPaused (hud: Hud) =
        let ctx = hud.Ctx
        dim hud 0.5
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontTitle
        fill ctx Cyan
        ctx.fillText ("PAUSE", hud.Width * 0.5, hud.Height * 0.36)
        prompt hud "P oder ENTER zum Fortsetzen" (hud.Height * 0.36 + 76.0)

    /// Game-Over-Bildschirm mit dem per setResult hinterlegten Ergebnis.
    let drawGameOver (hud: Hud) =
        let ctx = hud.Ctx
        let cx = hud.Width * 0.5
        let top = hud.Height * 0.26
        dim hud 0.55
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontTitle
        fill ctx Red
        ctx.fillText ("SCHIFF ZERSTOERT", cx, top)
        ctx.font <- FontValue
        fill ctx Amber
        ctx.fillText (hud.ResultScoreText, cx, top + 78.0)
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText (hud.ResultDetailText, cx, top + 118.0)
        fill ctx (if hud.ResultIsBest then Green else TextDim)
        ctx.fillText (hud.ResultBestText, cx, top + 144.0)
        prompt hud "ENTER oder Klick fuer einen neuen Versuch" (top + 196.0)
