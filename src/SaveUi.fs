namespace SpaceEngine

open Browser.Dom
open Browser.Types
open Fable.Core

type SaveUi =
    { Root: HTMLElement; Status: HTMLElement; Cards: HTMLElement
      Confirm: HTMLElement; ConfirmText: HTMLElement; CurrentExport: HTMLButtonElement
      mutable Visible: bool; mutable Action: string; mutable Slot: string
      mutable Pending: (string * string) option; mutable Imported: string; mutable ImportToken: int }

[<RequireQualifiedAccess>]
module SaveUi =
    [<Emit("new Date($0).toLocaleString('de-DE')")>]
    let date (value: float) : string = jsNative
    [<Emit("Date.now()")>]
    let now () : float = jsNative
    [<Emit("(()=>{const u=URL.createObjectURL(new Blob([$0],{type:'application/json'}));const a=document.createElement('a');a.href=u;a.download=$1;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(u),1000)})()")>]
    let download (json: string) (filename: string) : unit = jsNative
    [<Emit("(()=>{const f=$0.files&&$0.files[0];$0.value='';if(!f)return;if(f.size>2000000){$2('Datei zu groß (maximal 2 MB).');return;}f.text().then($1).catch(()=> $2('Die Datei konnte nicht gelesen werden.'));})()")>]
    let private readFile (input: HTMLInputElement) (success: string -> unit) (failure: string -> unit) : unit = jsNative
    [<Emit("(()=>{const scope=$1.hidden?$0:$1;const els=[...scope.querySelectorAll('button:not([disabled]),select:not([disabled]),input:not([disabled])')].filter(e=>e.offsetParent!==null);if(!els.length){$2.preventDefault();return;}const i=els.indexOf(document.activeElement);if(i<0||($2.shiftKey&&i===0)||(!$2.shiftKey&&i===els.length-1)){$2.preventDefault();els[$2.shiftKey?els.length-1:0].focus();}})()")>]
    let private trapTab (root: HTMLElement) (confirm: HTMLElement) (key: KeyboardEvent) : unit = jsNative
    [<Emit("$0.querySelectorAll('.save-header,#save-cards,.save-transfer,.save-footer').forEach(e=>{e.inert=$1;})")>]
    let private lockControls (root: HTMLElement) (locked: bool) : unit = jsNative

    let label slot = if slot = "auto" then "AUTOMATISCHER SPIELSTAND" else "SPEICHERPLATZ " + slot.Substring(4)
    let summary (data: SaveData) =
        let sector = Universe.tryFindSector data.Run.SectorId |> Option.get
        (if data.Run.Trading then "Handelsflug" else "Kampfmission") + " · " + sector.Outpost.Name + " · " + string data.Wallet.Credits + " CR"

    let create () =
        let element id = document.getElementById id
        let ui =
            { Root=element "save-panel"; Status=element "save-notice"; Cards=element "save-cards"
              Confirm=element "save-confirm"; ConfirmText=element "save-confirm-text"
              CurrentExport=unbox<HTMLButtonElement> (element "save-export-current")
              Visible=false;Action="";Slot="";Pending=None;Imported="";ImportToken=0 }
        for id, action in [|"open-saves","open";"save-close","close";"save-new-trade","newtrade";"save-new-combat","newcombat";"save-confirm-yes","confirm";"save-confirm-no","cancel";"save-export-current","exportcurrent"|] do
            (element id).addEventListener ("click", fun _ -> ui.Action <- action; ui.Slot <- "")
        (element "open-saves").addEventListener ("keydown",fun event ->
            let key = event :?> KeyboardEvent
            if key.code = "Enter" || key.code = "Space" then event.stopPropagation())
        let input = element "save-file" :?> HTMLInputElement
        (element "save-import").addEventListener ("click",fun _ -> input.click())
        input.addEventListener ("change",fun _ ->
            let slot = (element "save-import-slot" :?> HTMLSelectElement).value
            ui.ImportToken <- ui.ImportToken + 1
            let token = ui.ImportToken
            if ui.Pending.IsSome then input.value <- ""
            else readFile input
                    (fun json -> if token = ui.ImportToken && ui.Visible && ui.Pending.IsNone then ui.Imported <- json; ui.Action <- "import"; ui.Slot <- slot)
                    (fun error -> if token = ui.ImportToken && ui.Visible then ui.Status.textContent <- error))
        ui.Root.addEventListener ("keydown",fun event ->
            let key = event :?> KeyboardEvent
            event.stopPropagation()
            if key.code = "Tab" then trapTab ui.Root ui.Confirm key
            if key.code = "Escape" then
                event.preventDefault()
                ui.Action <- if ui.Pending.IsSome then "cancel" else "close")
        ui

    let render (ui: SaveUi) (entries: (string * SaveRead)[]) canSave =
        ui.CurrentExport.disabled <- not canSave
        ui.Cards.innerHTML <- ""
        for slot, entry in entries do
            let card = document.createElement "article"
            card.className <- "save-card"
            let heading = document.createElement "h2"
            heading.textContent <- label slot
            card.appendChild heading |> ignore
            let detail = document.createElement "p"
            let data = entry.Json |> Option.bind (fun json -> match GameSave.decode json with Ok value -> Some value | _ -> None)
            detail.textContent <- match data with Some value -> summary value | _ -> if entry.Error = "" then "Noch kein Spielstand." else entry.Error
            card.appendChild detail |> ignore
            let timestamp = document.createElement "small"
            timestamp.textContent <- match data with Some value -> date value.SavedAt + (if entry.FromBackup then " · SICHERUNGSKOPIE" else "") | _ -> ""
            card.appendChild timestamp |> ignore
            let actions = document.createElement "div"
            actions.className <- "save-actions"
            for action, title, enabled in [|"load","LADEN",data.IsSome; "save",(if slot="auto" then "JETZT SICHERN" else "SPEICHERN"),canSave; "export","EXPORT",data.IsSome|] do
                let button = document.createElement "button" :?> HTMLButtonElement
                button.textContent <- title
                button.disabled <- not enabled
                button.setAttribute("data-save-action",action)
                button.setAttribute("data-save-slot",slot)
                button.addEventListener("click",fun _ -> ui.Action <- action; ui.Slot <- slot)
                actions.appendChild button |> ignore
            card.appendChild actions |> ignore
            ui.Cards.appendChild card |> ignore

    let show (ui: SaveUi) =
        ui.ImportToken <- ui.ImportToken + 1
        ui.Visible <- true
        ui.Root.removeAttribute "hidden"
        document.exitPointerLock()
        (document.getElementById "save-close" :?> HTMLButtonElement).focus()

    let cancel (ui: SaveUi) =
        ui.Pending <- None
        ui.Confirm.setAttribute("hidden","")
        lockControls ui.Root false
        (document.getElementById "save-close" :?> HTMLButtonElement).focus()

    let hide (ui: SaveUi) =
        ui.ImportToken <- ui.ImportToken + 1
        ui.Visible <- false
        ui.Root.setAttribute("hidden","")
        ui.Pending <- None
        ui.Confirm.setAttribute("hidden","")
        lockControls ui.Root false

    let ask (ui: SaveUi) action slot message =
        ui.ImportToken <- ui.ImportToken + 1
        ui.Pending <- Some(action,slot)
        ui.ConfirmText.textContent <- message
        ui.Confirm.removeAttribute "hidden"
        lockControls ui.Root true
        (document.getElementById "save-confirm-no" :?> HTMLButtonElement).focus()
