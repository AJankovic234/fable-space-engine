namespace SpaceEngine

open Browser.Dom
open Browser.Types
open Fable.Core

type private ContactPhase = OnFile | Tuning | OnAir | TextOnly

type DockContact =
    private
        { Root: HTMLElement
          Name: HTMLElement
          Channel: HTMLElement
          Message: HTMLElement
          Transcript: HTMLElement
          Status: HTMLElement
          Action: HTMLButtonElement
          HomeButton: HTMLButtonElement
          Letter: HTMLElement
          LetterText: HTMLElement
          Sound: SoundEngine
          mutable Port: string
          mutable Home: Transmission option
          mutable Current: Transmission option
          mutable Requested: Transmission option
          mutable ArchiveState: (string * int * bool * bool) option
          mutable Phase: ContactPhase
          mutable NextAttempt: float
          mutable Deadline: float
          mutable StartedAt: float
          mutable Caption: int
          mutable Heard: int }

[<RequireQualifiedAccess>]
module DockContact =
    [<Emit("performance.now() / 1000")>]
    let private now () : float = jsNative

    let private stop ui =
        ui.Current |> Option.iter (fun call -> Sound.stopBeat ui.Sound call.Beat)

    let private refreshButtons ui =
        let buttons = document.querySelectorAll ("button[data-recording]")
        for i in 0 .. buttons.length-1 do
            let button = buttons.item(i) :?> HTMLButtonElement
            let beat = int (button.getAttribute "data-recording")
            let active = ui.Current |> Option.exists (fun call -> call.Beat = beat && ui.Phase <> OnFile)
            let seen = PilotSkills.contains ui.Heard beat
            button.textContent <-
                if active then
                    match ui.Phase with
                    | Tuning -> "Verbindung abbrechen"
                    | TextOnly -> "Gelesen"
                    | _ -> "Wiedergabe beenden"
                elif ui.Sound.Muted then (if seen then "Erneut lesen" else "Lesen")
                elif seen then "Erneut abhören"
                else "Abhören"
            button.setAttribute ("aria-pressed", if active then "true" else "false")
            button.setAttribute ("aria-label", button.textContent + " · " + button.title)

    let private setPhase ui phase =
        ui.Phase <- phase
        let state, status, action =
            match phase with
            | Tuning -> "tuning", "Verbindungsaufbau", "Überspringen"
            | OnAir -> "live", "Auf Sendung", "Funk beenden"
            | TextOnly -> "reading", "Textempfang", "Gelesen"
            | OnFile ->
                let seen = ui.Current |> Option.exists (fun call -> PilotSkills.contains ui.Heard call.Beat)
                let label = if ui.Sound.Muted then "Erneut lesen" elif seen then "Erneut abhören" else "Abhören"
                "archive", "Aufzeichnung", label
        ui.Root.setAttribute ("data-state", state)
        ui.Status.textContent <- status
        ui.Action.textContent <- action
        if phase = TextOnly || phase = OnFile then ui.Message.textContent <- ui.Transcript.textContent
        refreshButtons ui

    let private read ui =
        stop ui
        // Leave enough reading time for the whole recording, including its last line.
        ui.Deadline <- now() + max 6.0 (float ui.Transcript.textContent.Length / 15.0)
        setPhase ui TextOnly

    let private tune ui delay =
        if ui.Sound.Muted then read ui
        else
            ui.Current |> Option.iter (fun call -> ui.Message.textContent <- call.Subject)
            ui.NextAttempt <- now() + delay
            ui.Deadline <- ui.NextAttempt + 12.0
            setPhase ui Tuning

    let private select ui call =
        stop ui
        ui.Current <- Some call
        ui.Root.setAttribute ("data-portrait", call.Portrait)
        ui.Name.textContent <- call.Name
        ui.Channel.textContent <- call.Channel
        ui.Transcript.textContent <- StoryRadio.transcript call
        ui.Root.removeAttribute "hidden"
        if ui.Home |> Option.exists (fun home -> home.Beat <> call.Beat) then ui.HomeButton.removeAttribute "hidden"
        else ui.HomeButton.setAttribute ("hidden","")

    let private finish ui call =
        stop ui
        ui.Heard <- ui.Heard ||| (1 <<< call.Beat)
        setPhase ui OnFile

    let private toggle ui call =
        if (ui.Current |> Option.exists (fun active -> active.Beat = call.Beat)) && ui.Phase <> OnFile then
            finish ui call
        else
            select ui call
            tune ui 0.0

    let create sound =
        let element id = document.getElementById id
        let ui =
            { Root = element "dock-contact"; Name = element "contact-name"; Channel = element "contact-channel"
              Message = element "contact-message"; Transcript = element "contact-transcript"; Status = element "contact-status"
              Action = element "contact-action" :?> HTMLButtonElement; HomeButton = element "contact-home" :?> HTMLButtonElement
              Sound = sound; Letter = element "dock-letter"; LetterText = element "letter-text"
              Port = ""; Home = None; Current = None; Requested = None; ArchiveState = None; Phase = OnFile; NextAttempt = 0.0; Deadline = 0.0
              StartedAt = 0.0; Caption = 0; Heard = 0 }
        ui.Action.addEventListener ("click", fun _ -> ui.Requested <- ui.Current)
        ui.HomeButton.addEventListener ("click", fun _ -> ui.Requested <- ui.Home)
        ui

    let private appendRecording ui (parent: HTMLElement) (call: Transmission) =
        let details = document.createElement "details"
        let summary = document.createElement "summary"
        let text = document.createElement "p"
        let button = document.createElement "button" :?> HTMLButtonElement
        summary.textContent <- call.Subject
        text.textContent <- StoryRadio.transcript call
        button.setAttribute ("type","button")
        button.setAttribute ("data-recording",string call.Beat)
        button.title <- call.Subject
        button.addEventListener ("click", fun _ -> ui.Requested <- Some call)
        details.appendChild summary |> ignore
        details.appendChild text |> ignore
        details.appendChild button |> ignore
        parent.appendChild details |> ignore

    let refreshArchive ui pilot =
        let hasReveal = PilotSkills.level pilot 2 = 3 && PilotSkills.contains pilot.Relics 0
        let key = Some (ui.Port, pilot.Relics, StoryRadio.hasSideband pilot, hasReveal)
        if ui.ArchiveState <> key then
            ui.ArchiveState <- key
            let clips = StoryRadio.archive pilot
            let local = clips |> Array.filter (fun clip -> clip.Port = ui.Port)
            ui.LetterText.textContent <- ""
            if local.Length = 0 then ui.Letter.setAttribute ("hidden","")
            else
                for clip in local do appendRecording ui ui.LetterText clip.Call
                ui.Letter.removeAttribute "hidden"
            (document.getElementById "archive-count").textContent <- "Fundarchiv · " + string (PilotSkills.count pilot.Relics) + "/6"
            let log = document.getElementById "archive-entries"
            log.textContent <- ""
            RelicArchive.entries |> Array.iteri (fun i (name, body) ->
                if PilotSkills.contains pilot.Relics i then
                    let p = document.createElement "p"
                    let title = document.createElement "strong"
                    title.textContent <- name + " · "
                    p.appendChild title |> ignore
                    p.appendChild (document.createTextNode body) |> ignore
                    log.appendChild p |> ignore
                    for clip in clips do
                        if clip.Find = Some i then appendRecording ui log clip.Call)
            if pilot.Relics = 0 then
                log.textContent <- "Drücke im Stationsanflug G, um nach Aufzeichnungen zu suchen. Der Scanner erreicht 350 m rings um dein Schiff. Mit dem RT-9-Demodulator sind es 700 m."
            for clip in clips do
                if clip.Find.IsNone then appendRecording ui log clip.Call
            if hasReveal then
                let entry = document.createElement "p"
                entry.textContent <- "Stack-Abgleich · Die Prüfnummer aus Haldens Flugschreiber ist mir noch einmal begegnet. Mein Notfallprotokoll hat sie im eigenen Schiffskern gefunden. Ein Stück von Haldens Speicher war die ganze Zeit mit an Bord."
                log.appendChild entry |> ignore
            refreshButtons ui

    let leave ui =
        stop ui
        ui.Current <- None
        ui.Requested <- None
        ui.Home <- None
        ui.Port <- ""
        ui.ArchiveState <- None
        ui.Root.setAttribute ("hidden", "")
        ui.Letter.setAttribute ("hidden", "")

    let restore ui heard =
        leave ui
        ui.Heard <- heard

    let heard ui = ui.Heard

    let dock ui port pilot =
        leave ui
        ui.Port <- port
        ui.Home <- StoryRadio.atPort port
        refreshArchive ui pilot
        ui.Home |> Option.iter (fun call ->
            select ui call
            if PilotSkills.contains ui.Heard call.Beat then setPhase ui OnFile
            else tune ui 1.8)

    let onMuteChanged ui =
        match ui.Phase with
        | OnAir | Tuning when ui.Sound.Muted -> read ui
        | _ -> setPhase ui ui.Phase

    let update ui =
        ui.Requested |> Option.iter (toggle ui)
        ui.Requested <- None
        match ui.Current with
        | Some call ->
            let time = now()
            match ui.Phase with
            | Tuning when time >= ui.NextAttempt ->
                let duration = Sound.tryContactBeat ui.Sound call.Beat
                if duration > 0.0 then
                    ui.StartedAt <- time
                    ui.Caption <- 0
                    ui.Message.textContent <- snd call.Captions.[0]
                    ui.Deadline <- time + duration
                    setPhase ui OnAir
                elif duration < 0.0 || time >= ui.Deadline then read ui
                else ui.NextAttempt <- time + 0.25
            | OnAir when Sound.contactInterrupted ui.Sound call.Beat -> read ui
            | OnAir | TextOnly when time >= ui.Deadline -> finish ui call
            | OnAir ->
                let mutable cue = ui.Caption
                while cue + 1 < call.Captions.Length && fst call.Captions.[cue+1] <= time - ui.StartedAt do
                    cue <- cue + 1
                if cue <> ui.Caption then
                    ui.Caption <- cue
                    ui.Message.textContent <- snd call.Captions.[cue]
            | _ -> ()
        | None -> ()
