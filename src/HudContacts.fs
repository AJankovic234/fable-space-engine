[<RequireQualifiedAccess>]
module SpaceEngine.HudContacts

open System
open Browser.Types
open Fable.Core
open SpaceEngine

type Glyph = Square | Diamond | Circle | Bearing of dx: float * dy: float
type Marker =
    { X: float; Y: float; Radius: float; Glyph: Glyph; Tint: string
      Title: string; Detail: string; Priority: int; Quiet: bool }

let private fill (ctx: CanvasRenderingContext2D) color = ctx.fillStyle <- U3.Case1 color
let private stroke (ctx: CanvasRenderingContext2D) color = ctx.strokeStyle <- U3.Case1 color
let private labelFont = "600 12px ui-monospace, Consolas, monospace"
let private smallFont = "12px ui-monospace, Consolas, monospace"

let symbol (ctx: CanvasRenderingContext2D) glyph x y r =
    ctx.beginPath ()
    match glyph with
    | Square -> ctx.rect (x-r,y-r,r*2.0,r*2.0)
    | Circle -> ctx.arc (x,y,r,0.0,2.0*Math.PI)
    | Diamond ->
        ctx.moveTo (x,y-r)
        ctx.lineTo (x+r,y)
        ctx.lineTo (x,y+r)
        ctx.lineTo (x-r,y)
        ctx.closePath ()
    | Bearing (dx,dy) ->
        ctx.moveTo (x-dx*r-dy*r*0.7,y-dy*r+dx*r*0.7)
        ctx.lineTo (x+dx*r,y+dy*r)
        ctx.lineTo (x-dx*r+dy*r*0.7,y-dy*r-dx*r*0.7)
    ctx.stroke ()

let draw (ctx: CanvasRenderingContext2D) width height (reserved: ResizeArray<HudLayout.Rect>) (markers: ResizeArray<Marker>) =
    if markers.Count > 0 then
        ctx.save ()
        ctx.font <- labelFont
        ctx.textAlign <- "left"
        ctx.textBaseline <- "middle"
        let anchors: HudLayout.Anchor[] = Array.init markers.Count (fun i ->
            let m = markers.[i]
            let w =
                if m.Quiet || m.Title = "" then 0.0
                else ceil (max (ctx.measureText(m.Title).width) (ctx.measureText(m.Detail).width)) + 16.0
            { X = m.X; Y = m.Y; Radius = m.Radius; Width = w
              Height = if m.Detail = "" then 24.0 else 42.0
              Priority = m.Priority })
        for p in HudLayout.arrange width height (reserved.ToArray()) anchors do
            let m = markers.[p.Index]
            stroke ctx m.Tint
            if p.Symbol then
                ctx.globalAlpha <- if m.Quiet then 0.46 else 0.86
                ctx.lineWidth <- if m.Quiet then 1.25 else 1.7
                symbol ctx m.Glyph m.X m.Y m.Radius
            match p.Line with
            | Some l ->
                ctx.lineWidth <- 1.0
                ctx.globalAlpha <- 0.66
                ctx.beginPath ()
                ctx.moveTo (l.X1,l.Y1)
                ctx.lineTo (l.X2,l.Y2)
                ctx.stroke ()
            | None -> ()
            match p.Label with
            | Some r ->
                ctx.globalAlpha <- 1.0
                fill ctx "rgba(4,15,25,0.9)"
                ctx.fillRect (r.X,r.Y,r.W,r.H)
                fill ctx m.Tint
                ctx.fillRect (r.X,r.Y+3.0,2.0,r.H-6.0)
                ctx.font <- labelFont
                ctx.fillText (m.Title,r.X+8.0,r.Y+12.0)
                if m.Detail <> "" then
                    fill ctx "#c5d6dd"
                    ctx.font <- smallFont
                    ctx.fillText (m.Detail,r.X+8.0,r.Y+30.0)
            | None -> ()
        ctx.restore ()

let targetPanel (ctx: CanvasRenderingContext2D) (box: HudLayout.Rect) (enemy: Ship) (brain: AiBrain) distance closing training =
    let x,y,w = box.X,box.Y,box.W
    let name = if training then "Zieldrohne" else (EnemyProfiles.profile brain.Role).Name
    let charging = not training && brain.WeaponCharge > 0.0
    let state =
        if training then "UNBEWAFFNET" elif brain.FiringBurst then "FEUERSALVE"
        elif charging then "LÄDT AUF"
        else
            match brain.State with
            | AiState.Attack -> "ANGRIFF" | AiState.Evade -> "MANÖVER"
            | AiState.Patrol -> "PATROUILLE" | AiState.Chase -> "ANFLUG"
    ctx.save ()
    ctx.textAlign <- "left"
    ctx.textBaseline <- "middle"
    fill ctx "rgba(5,16,26,0.95)"
    ctx.fillRect (x,y,w,box.H)
    fill ctx "#ffce79"
    ctx.fillRect (x,y,3.0,box.H)
    ctx.font <- labelFont
    ctx.fillText ("ZIEL",x+12.0,y+16.0)
    fill ctx "#f4f4e9"
    ctx.font <- "600 16px ui-monospace, Consolas, monospace"
    ctx.fillText (name,x+54.0,y+16.0,w-65.0)
    ctx.font <- smallFont
    ctx.fillText (string (int (round distance)) + " m",x+12.0,y+36.0)
    ctx.textAlign <- "right"
    let speed = int (round closing)
    ctx.fillText ("Ann. " + (if speed > 0 then "+" else "") + string speed + " m/s",x+w-10.0,y+36.0)
    let meter left title value maximum tint =
        let fraction = if maximum > 0.0 then max 0.0 (min 1.0 (value/maximum)) else 0.0
        let barWidth = w*0.5-18.0
        ctx.textAlign <- "left"
        fill ctx "#c7d6e3"
        ctx.fillText (title,left,y+57.0)
        ctx.textAlign <- "right"
        let percent = if maximum > 0.0 then string (int (round (fraction*100.0))) + "%" else "–"
        ctx.fillText (percent,left+barWidth,y+57.0)
        fill ctx "#273846"
        ctx.fillRect (left,y+67.0,barWidth,4.0)
        fill ctx tint
        ctx.fillRect (left,y+67.0,barWidth*fraction,4.0)
    meter (x+12.0) "Schild" enemy.Shield enemy.MaxShield "#72baff"
    meter (x+w*0.5+6.0) "Hülle" enemy.Hull enemy.MaxHull "#eb997e"
    let urgent = charging || (brain.FiringBurst && not training)
    fill ctx (if urgent then "#663620" else "#172934")
    ctx.fillRect (x+3.0,y+80.0,w-3.0,24.0)
    fill ctx (if urgent then "#ffdc96" else "#cbdae0")
    ctx.font <- labelFont
    ctx.textAlign <- "left"
    ctx.fillText ((if urgent then "!  " else "") + state,x+12.0,y+92.0)
    if charging then
        fill ctx "#ffbc62"
        ctx.fillRect (x+4.0,y+101.0,(w-4.0)*min 1.0 brain.WeaponCharge,3.0)
    ctx.restore ()
