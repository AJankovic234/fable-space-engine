namespace SpaceEngine

open Browser.Dom
open Browser.Types
open Fable.Core.JsInterop

[<RequireQualifiedAccess>]
module StartHangar =
    let show trading launch =
        let get id = document.getElementById id
        let dialog = get "start-hangar"
        let opener = document.activeElement :?> HTMLElement
        let options = get "start-hulls"
        (get "start-lineup").textContent <- if trading then "Schwierigkeit im Handelsflug" else "Operation Morgenlicht · Schiffstypen"
        let mutable selected = 0
        let select index =
            selected <- index
            let hull = ShipCatalog.hulls.[index]
            let photo = get "start-portrait"
            photo.setAttribute ("src", "/assets/ships/" + hull.Art + ".webp")
            photo.setAttribute ("alt", hull.Name + " in der Dreiviertelansicht")
            (get "start-role").textContent <- hull.Role
            (get "start-name").textContent <- hull.Name
            (get "start-brief").textContent <-
                if trading then hull.FlightBrief
                else sprintf "Die Wendigkeit beträgt %.0f %% des Kutterwerts. Der erste Gegneranflug beginnt zwölf Sekunden nach dem Start." (100.0 * hull.Handling)
            (get "start-credits").textContent <- sprintf "%d CR" (ShipCatalog.startingCredits trading hull)
            (get "start-hold").textContent <- sprintf "%d t" hull.Hold
            (get "start-cruise").textContent <- sprintf "%g m/s" hull.Cruise
            (get "start-laser").textContent <- sprintf "%g" hull.Laser
        options.innerHTML <- ""
        for index in [| 3; 0; 1; 2 |] do
            let hull = ShipCatalog.hulls.[index]
            let card = document.createElement "label"
            card.className <- "start-option"
            card.innerHTML <-
                "<input type='radio' name='start-hull' value='" + hull.Art + "'>" +
                "<img alt='' src='/assets/ships/" + hull.Art + ".webp'>" +
                "<span><strong>" + hull.Name + "</strong><small>" + (if trading then hull.Difficulty else hull.Role) + "</small></span>"
            let radio = card.querySelector "input" :?> HTMLInputElement
            radio.``checked`` <- (index = 0)
            radio.addEventListener ("change", fun _ -> if radio.``checked`` then select index)
            options.appendChild card |> ignore
        select 0
        dialog?returnValue <- ""
        dialog?onclose <- fun () ->
            let focused = document.activeElement :?> HTMLElement
            if dialog.contains focused then focused.blur()
            if string dialog?returnValue = "launch" then launch selected
            elif opener = document.body && not (get "flight-menu").hidden then
                (get (if trading then "menu-new-game" else "menu-combat")).focus()
            elif not (isNull opener) then opener.focus()
        dialog?onkeydown <- fun (event: Event) -> event.stopPropagation()
        dialog?showModal()
        (options.querySelector "input:checked" :?> HTMLInputElement).focus()
