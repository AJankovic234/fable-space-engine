namespace SpaceEngine

open Browser.Dom
open Browser.Types
open Fable.Core.JsInterop

[<RequireQualifiedAccess>]
module StartHangar =
    let show launch =
        let get id = document.getElementById id
        let dialog = get "start-hangar"
        let options = get "start-hulls"
        let mutable selected = 0
        let select index =
            selected <- index
            let hull = ShipCatalog.hulls.[index]
            let photo = get "start-portrait"
            photo.setAttribute ("src", "/assets/ships/" + hull.Art + ".webp")
            photo.setAttribute ("alt", hull.Name + " in der Dreiviertelansicht")
            (get "start-role").textContent <- hull.Role
            (get "start-name").textContent <- hull.Name
            (get "start-brief").textContent <- hull.FlightBrief
            (get "start-credits").textContent <- sprintf "%d CR" hull.StartingCredits
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
                "<span><strong>" + hull.Name + "</strong><small>" + hull.Difficulty + "</small></span>"
            let radio = card.querySelector "input" :?> HTMLInputElement
            radio.``checked`` <- (index = 0)
            radio.addEventListener ("change", fun _ -> if radio.``checked`` then select index)
            options.appendChild card |> ignore
        select 0
        dialog?returnValue <- ""
        dialog?onclose <- fun () -> if string dialog?returnValue = "launch" then launch selected
        dialog?onkeydown <- fun (event: Event) -> event.stopPropagation()
        dialog?showModal()
        (options.querySelector "input:checked" :?> HTMLInputElement).focus()
