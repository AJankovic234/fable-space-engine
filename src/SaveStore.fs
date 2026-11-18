namespace SpaceEngine

open System
open Fable.Core

/// Der Browseradapter wird vom Aufrufer bereitgestellt; Modulimport greift auf keinen Speicher zu.
type SaveStorage =
    { Read: string -> string
      Write: string -> string -> unit }

type SaveRead =
    { Json: string option
      FromBackup: bool
      Error: string }

type SaveWrite =
    { Ok: bool
      Error: string }

[<RequireQualifiedAccess>]
module SaveStore =
    let slots = [| "auto"; "slot1"; "slot2"; "slot3" |]

    let key (slot: string) = "fable-space-engine.save." + slot

    let private validSlot slot =
        match slot with
        | "auto" | "slot1" | "slot2" | "slot3" -> true
        | _ -> false

    [<Emit("JSON.parse($0)")>]
    let private parseJson (json: string) : obj = jsNative

    let private validJson (validate: string -> bool) (json: string) =
        if String.IsNullOrWhiteSpace json then false
        else
            try
                parseJson json |> ignore
                validate json
            with _ -> false

    type private StoredContent =
        | Missing
        | Valid of string
        | Invalid
        | Unreadable

    let private inspect (storage: SaveStorage) storageKey validate =
        try
            let json = storage.Read storageKey
            if isNull json then Missing
            elif validJson validate json then Valid json
            else Invalid
        with _ -> Unreadable

    /// Liest nur. Wiederherstellung aus der Sicherung schreibt keinen Schluessel um.
    let read (storage: SaveStorage) (slot: string) (validate: string -> bool) : SaveRead =
        if not (validSlot slot) then
            { Json = None; FromBackup = false; Error = "Unbekannter Speicherplatz." }
        else
            let storageKey = key slot
            let primary = inspect storage storageKey validate
            match primary with
            | Valid json -> { Json = Some json; FromBackup = false; Error = "" }
            | _ ->
                let backup = inspect storage (storageKey + ".backup") validate
                match primary, backup with
                | _, Valid json ->
                    { Json = Some json
                      FromBackup = true
                      Error = "Der Hauptspielstand fehlt oder ist nicht lesbar. Eine gültige Sicherung wurde geladen." }
                | Missing, Missing -> { Json = None; FromBackup = false; Error = "" }
                | Unreadable, _ | _, Unreadable ->
                    { Json = None
                      FromBackup = false
                      Error = "Der Browser-Speicher konnte nicht gelesen werden. Bitte eine gültige Spielstanddatei importieren." }
                | _ ->
                    { Json = None
                      FromBackup = false
                      Error = "Kein gültiger Spielstand verfügbar: Spielstand oder Sicherung sind beschädigt. Bitte eine Spielstanddatei importieren." }

    /// Schreibt synchron: erst die letzte valide Hauptversion sichern, dann die neue Version.
    /// Ein defekter Hauptstand ersetzt niemals eine vorhandene Sicherung.
    let write (storage: SaveStorage) (slot: string) (json: string) (validate: string -> bool) : SaveWrite =
        if not (validSlot slot) then
            { Ok = false; Error = "Unbekannter Speicherplatz." }
        elif not (validJson validate json) then
            { Ok = false; Error = "Spielstand ist ungültig und wurde nicht gespeichert." }
        else
            let storageKey = key slot
            match inspect storage storageKey validate with
            | Unreadable ->
                { Ok = false
                  Error = "Der vorhandene Spielstand konnte nicht gelesen werden. Bitte den aktuellen Spielstand als Datei exportieren." }
            | previous ->
                try
                    match previous with
                    | Valid oldJson -> storage.Write (storageKey + ".backup") oldJson
                    | _ -> ()
                    storage.Write storageKey json
                    { Ok = true; Error = "" }
                with _ ->
                    { Ok = false
                      Error = "Speichern im Browser nicht möglich (Speicher voll oder gesperrt). Vorhandene Spielstände wurden nicht gelöscht. Bitte den aktuellen Spielstand als Datei exportieren." }
