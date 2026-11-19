/// Bildschirmgeometrie in CSS-Pixeln. Keine Projektion und kein Canvas-Zustand.
[<RequireQualifiedAccess>]
module SpaceEngine.HudLayout

type Rect = { X: float; Y: float; W: float; H: float }
type Anchor = { X: float; Y: float; Radius: float; Width: float; Height: float; Priority: int }
type Leader = { X1: float; Y1: float; X2: float; Y2: float }
type Placement = { Index: int; Symbol: bool; Label: Rect option; Line: Leader option }

let rect x y w h = { X = x; Y = y; W = w; H = h }
let around x y radius = rect (x-radius) (y-radius) (2.0*radius) (2.0*radius)

/// Kontakte ausser Reichweite bleiben auf dem Rand. Die Hoehenlinie liegt auf
/// der Seite der Ebene, auf der sich der Kontakt relativ zum Schiff befindet.
let radarOffset range rx ry (local: Vec3) =
    let scale = max range (sqrt (local.X*local.X + local.Z*local.Z) / 0.94)
    let foot = local.Z/scale * ry
    let elevation = min 1.0 (abs local.Y / range)
    let tip = foot - float (sign local.Y) * sqrt elevation * 17.0
    struct (local.X/scale * rx, foot, max -36.0 (min 30.0 tip))

let private overlaps (a: Rect) (b: Rect) =
    a.X < b.X+b.W && b.X < a.X+a.W && a.Y < b.Y+b.H && b.Y < a.Y+a.H

let private inside width height (r: Rect) =
    r.X >= 4.0 && r.Y >= 4.0 && r.X+r.W <= width-4.0 && r.Y+r.H <= height-4.0

/// Stabile Instrumentenplaetze, bevorzugt bei den Schiffswerten. Enge Ansichten
/// verwenden dessen Platz. Visier und bestehende Anzeigen bestimmen das Ausweichfach.
let targetBox width height walletVisible (sight: Rect) (reserved: ResizeArray<Rect>) =
    let w,h = min 220.0 (width-24.0),104.0
    let right = width-w-16.0
    let top = if walletVisible then 163.0 elif width < 680.0 then 126.0 else 38.0
    let box x y = rect x (max 4.0 (min (height-h-4.0) y)) w h
    let area (a: Rect) (b: Rect) =
        max 0.0 (min (a.X+a.W) (b.X+b.W) - max a.X b.X) *
        max 0.0 (min (a.Y+a.H) (b.Y+b.H) - max a.Y b.Y)
    let aboveGauges = box 16.0 (height-290.0)
    let byWallet = box right top
    let choices =
        [| if walletVisible then aboveGauges else byWallet
           if walletVisible then byWallet else aboveGauges
           box 16.0 126.0; box right (height-278.0)
           box right 38.0; box 16.0 (height-h-12.0) |]
    let mutable best = choices.[0]
    let mutable cost = infinity
    let mutable i = 0
    while i < choices.Length && cost > 0.0 do
        let r = choices.[i]
        let score = area r sight * 1000.0 + (reserved |> Seq.sumBy (area r))
        if score < cost then
            best <- r
            cost <- score
        i <- i + 1
    best

/// Ein horizontaler/vertikaler Verweis beginnt erst ausserhalb des Ankers und
/// seiner Schutzflaechen. Andere Marker und bereits gesetzte Texte sperren den Weg.
let private leader (occupied: ResizeArray<Rect>) (a: Anchor) dx dy reach =
    let mutable start = a.Radius + 2.0
    let mutable clear = true
    for r in occupied do
        let horizontal = dx <> 0.0
        let crosses =
            if horizontal then a.Y >= r.Y-1.0 && a.Y <= r.Y+r.H+1.0
            else a.X >= r.X-1.0 && a.X <= r.X+r.W+1.0
        if crosses then
            let lo, hi =
                if dx > 0.0 then r.X-a.X, r.X+r.W-a.X
                elif dx < 0.0 then a.X-r.X-r.W, a.X-r.X
                elif dy > 0.0 then r.Y-a.Y, r.Y+r.H-a.Y
                else a.Y-r.Y-r.H, a.Y-r.Y
            if lo <= 0.0 && hi >= 0.0 then start <- max start (hi+2.0)
            elif hi > start && lo < reach then clear <- false
    let line =
        if reach <= start + 2.0 then None
        else Some { X1 = a.X+dx*start; Y1 = a.Y+dy*start; X2 = a.X+dx*reach; Y2 = a.Y+dy*reach }
    clear, line

/// Wichtige Symbole behalten ihren Weltpunkt. Texte suchen freie Plaetze an vier
/// Seiten; reicht der Platz nicht, entfaellt nur die jeweilige Beschriftung.
let arrange width height (reserved: Rect[]) (anchors: Anchor[]) =
    let occupied = ResizeArray<Rect>(reserved)
    let free r = inside width height r && not (occupied.Exists(fun other -> overlaps r other))
    let order = Array.init anchors.Length id |> Array.sortBy (fun i -> -anchors.[i].Priority, i)
    let placements = order |> Array.map (fun i ->
        let a = anchors.[i]
        let bounds = around a.X a.Y (a.Radius+3.0)
        let visible = free bounds
        if visible then occupied.Add bounds
        { Index = i; Symbol = visible; Label = None; Line = None })
    for k in 0 .. placements.Length-1 do
        let p = placements.[k]
        let a = anchors.[p.Index]
        if a.Width > 0.0 && a.Height > 0.0 then
            let side = if a.X < width*0.5 then -1.0 else 1.0
            let mutable trial = 0
            let mutable placed = false
            while not placed && trial < 16 do
                let dx,dy =
                    match trial % 4 with
                    | 0 -> side,0.0 | 1 -> -side,0.0 | 2 -> 0.0,1.0 | _ -> 0.0,-1.0
                let reach = a.Radius + 15.0 + float (trial / 4) * 32.0
                let x =
                    if dx > 0.0 then a.X+reach elif dx < 0.0 then a.X-reach-a.Width
                    else max 4.0 (min (width-4.0-a.Width) (a.X-a.Width*0.5))
                let y =
                    if dy > 0.0 then a.Y+reach elif dy < 0.0 then a.Y-reach-a.Height
                    else max 4.0 (min (height-4.0-a.Height) (a.Y-a.Height*0.5))
                let box = rect x y a.Width a.Height
                if free box then
                    let clear,line = leader occupied a dx dy reach
                    if clear then
                        placements.[k] <- { p with Label = Some box; Line = line }
                        occupied.Add(rect (x-3.0) (y-3.0) (a.Width+6.0) (a.Height+6.0))
                        match line with
                        | Some l -> occupied.Add(rect (min l.X1 l.X2-1.0) (min l.Y1 l.Y2-1.0) (abs(l.X2-l.X1)+2.0) (abs(l.Y2-l.Y1)+2.0))
                        | None -> ()
                        placed <- true
                trial <- trial + 1
    placements
