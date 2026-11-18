namespace SpaceEngine

open Browser.Dom
open Browser.Types

type SkillUi =
    { Buttons: HTMLButtonElement[]; Notes: HTMLElement[]
      mutable Request: (int * int) option }

[<RequireQualifiedAccess>]
module SkillUi =
    let private branches = [| "Signal-Archäologe"; "Schatten-Fixer"; "Sleeve-Gehärtet" |]
    let private nodes =
        [| "RT-9-Demodulator", "Magnetreichweite 55 → 85 m. Verdoppelt den Radius deines Archivscans (G) von 350 auf 700 m.", "Sera hört auf dem analogen Nebenband von RT-9 noch den Träger vom 14. März 2034."
           "Cortical-Forensik", "Kanonenboote und Bomber hinterlassen eine Kapsel mit 1 t Nav-Daten. Auf deren positive Verkaufsmarge erhältst du 35 % zusätzlich.", "Im CS-7.3-Handbuch endet die Haftung dort, wo eine Kennung auf zwei Sender antwortet."
           "Geister-Protokoll", "V sendet einen Störimpuls. Gegner regenerieren 8 s keine Schilde. Drohnen stellen 3 s das Feuer ein. Nach 45 s erneut bereit.", "Linds Band enthält zwei zeitgleiche Antworten auf dieselbe TG-4-Anfrage."
           "Gassen-Chiffre", "25 % Rabatt auf Geleitschutz, Frachtraumausbau und die drei Schiffsmodule. Routenkarten zeigen Angebot und Nachfrage.", "Orlov führt die offenen Sauerstoffschulden der Frühschicht auf Papier."
           "Doppel-Transponder", "Sofort 8 t mehr Platz, ausgebaut bis 48 t. Auf ungeschützten Routen kommen Piraten nach 20 statt 8 s.", "Kipper lässt zwei Frachträume unter derselben Kennung laufen. Die zweite Anmeldung kommt zwölf Sekunden später."
           "Schatten-Rat", "15 % zusätzlich auf jede positive Verkaufsmarge. Ein gekaufter Auto-Turret feuert 40 % schneller. 25 % seines Schadens gehen direkt auf die Hülle.", "Das Dock verbucht die Teile für deinen Turm als Werkzeug für einen Erzverladekran."
           "Phantom-Muskelgedächtnis", "Laser und Wendigkeit +20 %. Reisegeschwindigkeit 60 → 66 m/s. Die Retro-Bremse verzögert 35 % stärker.", "Der Bewegungsablauf stammt aus einer alten ESA-Notfallcheckliste. Dein Sleeve kennt ihn schon."
           "Adrenalin-Bypass", "Beim Schildbruch 4 s Overdrive, frühestens alle 25 s. Jeder Abschuss lädt 31 statt 16 Schildpunkte.", "Der Bypass sitzt unter dem alten medizinischen Anschluss des Sleeves."
           "Die Achte Kopie", "Der erste tödliche Treffer je Sektor lässt dich mit 30 % Hülle, vollem Schild und 3 s Overdrive weiterfliegen.", "Das lokale Notprotokoll hält deinen beschädigten Sleeve am Leben. Im Platin-Register entsteht kein Backup." |]

    let create () =
        let root = document.getElementById "skill-branches"
        let buttons = ResizeArray<HTMLButtonElement>()
        let notes = ResizeArray<HTMLElement>()
        for branch in 0..2 do
            let column = document.createElement "section"
            let heading = document.createElement "h3"
            heading.textContent <- branches.[branch]
            column.appendChild heading |> ignore
            for rank in 1..3 do
                let name, effect, note = nodes.[branch*3+rank-1]
                let node = document.createElement "article"
                node.innerHTML <- "<small>STUFE " + string rank + "</small><h4>" + name + "</h4><p>" + effect + "</p><button type='button'></button><p class='skill-note' hidden></p>"
                let button = node.querySelector("button") :?> HTMLButtonElement
                button.setAttribute ("data-skill", string (branch*3+rank-1))
                let entry = node.querySelector(".skill-note") :?> HTMLElement
                entry.textContent <- note
                notes.Add entry
                buttons.Add button
                column.appendChild node |> ignore
            root.appendChild column |> ignore
        let ui = { Buttons = buttons.ToArray(); Notes = notes.ToArray(); Request = None }
        ui.Buttons |> Array.iteri (fun i b -> b.addEventListener ("click", fun _ -> ui.Request <- Some (i/3, i%3+1)))
        ui

    let refresh ui pilot =
        let available = PilotSkills.points pilot
        (document.getElementById "skill-points").textContent <- string available + (if available = 1 then " Stack-Punkt frei" else " Stack-Punkte frei")
        let tab = document.getElementById "skills-balance"
        tab.textContent <- if available > 0 then string available else ""
        ui.Buttons |> Array.iteri (fun i b ->
            let branch, rank = i/3, i%3+1
            let current, cost = PilotSkills.level pilot branch, PilotSkills.cost rank
            let state, label =
                if rank <= current then "learned", "Gelernt"
                elif rank > current + 1 then "locked", "Benötigt Stufe " + string (rank-1)
                elif available < cost then "locked", string cost + (if cost = 1 then " Punkt nötig" else " Punkte nötig")
                else "ready", "Lernen · " + string cost + (if cost = 1 then " Punkt" else " Punkte")
            b.disabled <- state <> "ready"
            b.textContent <- label
            let name, _, _ = nodes.[i]
            b.setAttribute ("aria-label", name + " · " + label)
            b.parentElement.setAttribute ("data-state", state)
            if state = "learned" then ui.Notes.[i].removeAttribute "hidden"
            else ui.Notes.[i].setAttribute ("hidden",""))
