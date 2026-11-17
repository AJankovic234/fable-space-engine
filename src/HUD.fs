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
      mutable MenuShade: CanvasGradient
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
      /// true, wenn das Ziel manuell per T/Tab/RMB aufgeschaltet wurde.
      mutable ManualLock: bool
      /// Aktueller Kill-Kombo-Multiplikator (1..4) und Restzeit.
      mutable Combo: int
      mutable ComboTimer: float
      mutable Muted: bool
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
          MenuShade = Unchecked.defaultof<CanvasGradient>
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
          ManualLock = false
          Combo = 1
          ComboTimer = 0.0
          Muted = false
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
        let shade = hud.Ctx.createLinearGradient (0.0, 0.0, hud.Width, 0.0)
        shade.addColorStop (0.0, "rgba(3, 9, 18, 0.96)")
        shade.addColorStop (0.36, "rgba(3, 9, 18, 0.82)")
        shade.addColorStop (0.72, "rgba(3, 9, 18, 0.12)")
        shade.addColorStop (1.0, "rgba(3, 9, 18, 0.04)")
        hud.MenuShade <- shade

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

    /// Zeichnet die Cockpit-Kanzelstreben in der Ich-Perspektive.
    let private drawCockpitCanopy (ctx: CanvasRenderingContext2D) (w: float) (h: float) =
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

    /// 3D-Sensorschirm (Gravidar) unten in der Bildmitte: zeigt Gegner und nahe
    /// Gross-Asteroiden relativ zur Ausrichtung des eigenen Schiffs mit Hoehenlinien.
    let private drawRadar
        (hud: Hud)
        (world: World)
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
        let radarRange = 460.0

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

        // 2. Gegner mit vertikalen Hoehenlinien (Stalks)
        let enemies = world.Enemies

        for i in 0 .. enemies.Length - 1 do
            let enemy = enemies.[i]

            if enemy.Alive then
                let ex = enemy.PrevPosition.X + (enemy.Position.X - enemy.PrevPosition.X) * alpha
                let ey = enemy.PrevPosition.Y + (enemy.Position.Y - enemy.PrevPosition.Y) * alpha
                let ez = enemy.PrevPosition.Z + (enemy.Position.Z - enemy.PrevPosition.Z) * alpha
                let vx = ex - playerPosition.X
                let vy = ey - playerPosition.Y
                let vz = ez - playerPosition.Z
                let tx = 2.0 * (qy * vz - qz * vy)
                let ty = 2.0 * (qz * vx - qx * vz)
                let tz = 2.0 * (qx * vy - qy * vx)
                let mutable lx = (vx + qw * tx + (qy * tz - qz * ty)) / radarRange
                let mutable ly = (vy + qw * ty + (qz * tx - qx * tz)) / radarRange
                let mutable lz = (vz + qw * tz + (qx * ty - qy * tx)) / radarRange
                let planar = sqrt (lx * lx + lz * lz)

                if planar > 0.96 then
                    let inv = 0.96 / planar
                    lx <- lx * inv
                    lz <- lz * inv

                ly <- Math.Max(-0.9, Math.Min(0.9, ly))
                let discX = cx + lx * rx
                let discY = baseY + lz * ry
                let blipY = discY - ly * 22.0
                let isTarget = i = target

                // Fusspunkt auf der Scheibe + Hoehenlinie
                stroke ctx (if isTarget then Amber else RedSoft)
                ctx.lineWidth <- 1.2
                ctx.beginPath ()
                ctx.moveTo (discX, discY)
                ctx.lineTo (discX, blipY)
                ctx.stroke ()
                fill ctx (if isTarget then Amber else Red)
                ctx.beginPath ()
                ctx.arc (discX, blipY, (if isTarget then 3.4 else 2.5), 0.0, 2.0 * Math.PI)
                ctx.fill ()

        // Eigenes Schiff in der Mitte
        fill ctx Cyan
        ctx.fillRect (cx - 2.0, baseY - 2.0, 4.0, 4.0)
        ctx.textAlign <- "center"
        ctx.textBaseline <- "top"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText ("GRAVIDAR", cx, cy - 46.0)

    /// Das Flug-HUD.
    ///   viewProj/aspect : Kamera des aktuellen Frames
    ///   alpha           : Interpolationsfaktor zwischen den letzten Physikzustaenden
    ///   playerPosition/-Rotation : interpolierte Darstellungspose des Spielers
    let drawFlight
        (hud: Hud)
        (viewProj: Mat4)
        (aspect: float)
        (cameraMode: CameraMode)
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

        if cameraMode = Cockpit && player.Alive then
            drawCockpitCanopy ctx w h

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
                        // Getrennte Balken fuer Schild (blau) und Huelle (rot)
                        let shieldFrac = enemy.Shield / Math.Max(1.0, enemy.MaxShield)
                        let hullFrac = enemy.Hull / Math.Max(1.0, enemy.MaxHull)
                        fill ctx CyanDim
                        ctx.fillRect (x - size, y - size - 13.0, size * 2.0, 3.0)
                        fill ctx ShieldBlue
                        ctx.fillRect (x - size, y - size - 13.0, size * 2.0 * shieldFrac, 3.0)
                        fill ctx RedDim
                        ctx.fillRect (x - size, y - size - 8.0, size * 2.0, 3.0)
                        fill ctx Red
                        ctx.fillRect (x - size, y - size - 8.0, size * 2.0 * hullFrac, 3.0)

                        let stateLabel =
                            match brains.[i].State with
                            | AiState.Attack -> "ANGRIFF"
                            | AiState.Chase -> "ANFLUG"
                            | AiState.Evade -> "MANOEVER"
                            | AiState.Patrol -> "PATROUILLE"

                        fill ctx (if attacking then Red else TextColor)
                        ctx.fillText (number hud distance + "m · " + stateLabel, x, y + size + 11.0)
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
        let barW = Math.Min(190.0, w - 158.0)
        let baseY = h - 132.0
        let hullFraction = player.Hull / player.MaxHull
        let critical = hullFraction < 0.3
        fill ctx "rgba(3, 12, 23, 0.72)"
        ctx.fillRect (left - 12.0, baseY - 18.0, barW + 118.0, 140.0)
        fill ctx Cyan
        ctx.fillRect (left - 12.0, baseY - 18.0, 34.0, 2.0)
        stroke ctx CyanDim
        ctx.strokeRect (left - 12.0, baseY - 18.0, barW + 118.0, 140.0)
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
            drawRadar hud world alpha playerPosition playerRotation target

        // ---- Unten rechts: Welle, Gegner, Punkte + Kill-Kombo ----
        if score <> hud.ScoreValue then
            hud.ScoreValue <- score
            hud.ScoreText <- string score

        let right = w - 24.0
        let scoreBottom = if w < 680.0 then 178.0 else h

        if hud.Combo > 1 && hud.ComboTimer > 0.0 then
            fill ctx "rgba(3, 12, 23, 0.78)"
            ctx.fillRect (right - 190.0, scoreBottom - 166.0, 202.0, 34.0)
            stroke ctx Amber
            ctx.strokeRect (right - 190.0, scoreBottom - 166.0, 202.0, 34.0)
            ctx.textAlign <- "left"
            ctx.font <- FontLabel
            fill ctx Amber
            ctx.fillText ("KOMBO x" + hud.Numbers.[hud.Combo], right - 178.0, scoreBottom - 158.0)
            bar ctx (right - 96.0) (scoreBottom - 154.0) 96.0 6.0 (hud.ComboTimer / 8.0) Amber

        fill ctx "rgba(3, 12, 23, 0.72)"
        ctx.fillRect (right - 190.0, scoreBottom - 126.0, 202.0, 116.0)
        stroke ctx CyanDim
        ctx.strokeRect (right - 190.0, scoreBottom - 126.0, 202.0, 116.0)
        fill ctx Amber
        ctx.fillRect (right - 22.0, scoreBottom - 126.0, 34.0, 2.0)
        ctx.textAlign <- "right"
        ctx.font <- FontLabel
        fill ctx TextColor
        ctx.fillText ("WELLE", right - 78.0, scoreBottom - 98.0)
        ctx.fillText ("GEGNER", right - 78.0, scoreBottom - 68.0)
        ctx.fillText ("PUNKTE", right - 78.0, scoreBottom - 38.0)
        ctx.font <- FontValue
        fill ctx Cyan
        ctx.fillText (number hud (float wave), right, scoreBottom - 105.0)
        fill ctx (if enemiesAlive > 0 then Red else TextDim)
        ctx.fillText (number hud (float enemiesAlive), right, scoreBottom - 75.0)
        fill ctx Amber
        ctx.fillText (hud.ScoreText, right, scoreBottom - 45.0)

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

        // ---- Statistik oben links & Systemstatus oben rechts ----
        ctx.textAlign <- "left"
        ctx.font <- FontSmall
        fill ctx TextDim
        ctx.fillText (hud.StatsText, 16.0, 12.0)

        ctx.textAlign <- "right"
        let camLabel = if cameraMode = Cockpit then "COCKPIT" else "VERFOLGER"
        let soundLabel = if hud.Muted then "TON AUS" else "TON AN"
        ctx.fillText ("[C] " + camLabel + "   [T] ZIEL   [M] " + soundLabel, right, 12.0)

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
        ctx.fillStyle <- U3.Case2 hud.MenuShade
        ctx.fillRect (0.0, 0.0, hud.Width, hud.Height)
        let scale = Math.Min(1.0, Math.Min((hud.Width - 48.0) / 430.0, (hud.Height - 60.0) / 660.0))
        let left = if hud.Width > 1000.0 then 64.0 else 28.0
        let top = Math.Max(28.0, (hud.Height - 660.0 * scale) * 0.44)
        ctx.save ()
        ctx.translate (left, top)
        ctx.scale (scale, scale)
        ctx.textAlign <- "left"
        ctx.textBaseline <- "top"
        ctx.font <- FontLabel
        fill ctx Cyan
        ctx.fillRect (0.0, 2.0, 22.0, 2.0)
        ctx.fillText ("F L U G B E R E I T  /  0 1", 34.0, -3.0)
        ctx.font <- "800 86px Bahnschrift, Segoe UI, sans-serif"
        fill ctx "#f0f6f5"
        ctx.fillText ("FABLE", -5.0, 34.0)
        fill ctx Cyan
        ctx.fillText ("SPACE", -5.0, 114.0)
        ctx.font <- "14px Segoe UI, sans-serif"
        fill ctx TextColor
        ctx.fillText ("Ein Schiff. Ein Asteroidenfeld. Kein sicherer Kurs.", 0.0, 222.0)
        ctx.fillText ("Überlebe die Angriffswellen und schlage deinen Rekord.", 0.0, 245.0)
        stroke ctx CyanDim
        ctx.beginPath ()
        ctx.moveTo (0.0, 287.0)
        ctx.lineTo (416.0, 287.0)
        ctx.stroke ()
        ctx.font <- FontLabel
        fill ctx TextDim
        ctx.fillText ("FLUGSTEUERUNG", 0.0, 307.0)
        let rowY = 338.0
        menuRow ctx 162.0 rowY "W / S" "Nase runter / hoch"
        menuRow ctx 162.0 (rowY + 23.0) "A / D · Q / E" "Rollen · Gieren"
        menuRow ctx 162.0 (rowY + 46.0) "↑ / ↓" "Schub einstellen"
        menuRow ctx 162.0 (rowY + 69.0) "SHIFT · X" "Boost · Bremse"
        menuRow ctx 162.0 (rowY + 92.0) "LEERTASTE / LMB" "Feuern"
        menuRow ctx 162.0 (rowY + 115.0) "T / TAB / RMB" "Ziel aufschalten"
        menuRow ctx 162.0 (rowY + 138.0) "MAUS NACH KLICK" "Schiff ausrichten"
        menuRow ctx 162.0 (rowY + 161.0) "C · M · P" "Kamera · Ton · Pause"
        ctx.textAlign <- "left"
        fill ctx "rgba(118, 229, 249, 0.12)"
        ctx.fillRect (0.0, 542.0, 416.0, 56.0)
        stroke ctx "rgba(118, 229, 249, 0.6)"
        ctx.strokeRect (0.0, 542.0, 416.0, 56.0)
        fill ctx Cyan
        ctx.fillRect (0.0, 542.0, 3.0, 56.0)
        ctx.font <- "600 15px Segoe UI, sans-serif"
        ctx.fillText ("FLUG STARTEN", 20.0, 560.0)
        ctx.font <- FontSmall
        ctx.textAlign <- "right"
        fill ctx TextColor
        ctx.fillText ("ENTER / KLICK  →", 396.0, 562.0)
        ctx.textAlign <- "left"
        fill ctx TextDim
        ctx.fillText (hud.BestText, 0.0, 620.0)
        ctx.restore ()

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
