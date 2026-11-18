namespace SpaceEngine

type Transmission =
    { Portrait: string
      Name: string
      Channel: string
      Subject: string
      Captions: (float * string)[]
      Beat: int }

type ArchiveRecording =
    { Call: Transmission
      Port: string
      Find: int option }

[<RequireQualifiedAccess>]
module StoryRadio =
    // Savegames use the recording index as its permanent bit position.
    let heardMask = (1 <<< 18) - 1

    let transcript call = call.Captions |> Array.map snd |> String.concat " "

    let atPort = function
        | "station-kestrel" ->
            Some { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 0
                   Subject = "Abgleich deiner Schiffskennung"
                   Captions = [| 0.0, "Hier Voss auf Kestrel."; 2.1, "Zwei Schiffe, eine Kennung."
                                 4.3, "Schau nicht auf den Monitor. Vertrau nur deinem Radar." |] }
        | "station-helios" ->
            Some { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 12
                   Subject = "Nachricht vom Werkstor"
                   Captions = [| 0.0, "Willkommen auf Helios IV."; 2.4, "Halt medizinische Vorräte aus dem Zollscan."
                                 5.6, "Unsere Lungen zahlen keinen Tarif." |] }
        | "station-bazaar" ->
            Some { Portrait = "kipper"; Name = "Kipper Taro"; Channel = "Drift-9 · Fixer-Frequenz"; Beat = 7
                   Subject = "Dein neuer Transponder"
                   Captions = [| 0.0, "Hier Kipper auf Drift-9."; 1.9, "Dein neuer Transponder klebt mit Isolierband,"
                                 4.0, "aber gegen Vances Drohnen hält er."; 6.3, "Hoff ich." |] }
        | "station-kepler" ->
            Some { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Fernleitung nach Kepler"; Beat = 3
                   Subject = "Lizenzprüfung in der Orbitalwerft"
                   Captions = [| 0.0, "Kepler-07 ist kein Kriegsgebiet, sondern eine vergessene Lizenzliste."
                                 4.5, "Die Drohnen schießen auf jeden ohne Stempel." |] }
        | "station-aether" ->
            Some { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 4
                   Subject = "Das entschlüsselte Archivband"
                   Captions = [| 0.0, "Ich habe das alte Archivband entschlüsselt."
                                 2.5, "Derselbe Fehler wie bei ORPHEUS-7, nur für einen ganzen Sektor." |] }
        | "station-bastion" ->
            Some { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Sovereign · Direktleitung"; Beat = 6
                   Subject = "Persönlicher Ruf der Direktion"
                   Captions = [| 0.0, "Oh, niedlich."; 1.4, "Ein rostiger Erzfrachter spielt Händler."
                                 4.3, "Genieß deine Credits, Kleiner."; 6.2, "Ich lösch dich gleich aus dem System." |] }
        | _ -> None

    let private recordings =
        [| { Port = "station-kestrel"; Find = Some 0
             Call = { Portrait = "halden"; Name = "J. Halden"; Channel = "Kestrel-04 · Flugschreiber"; Beat = 17
                      Subject = "Haldens letzter Eintrag"
                      Captions = [| 0.0, "Hier Halden auf Kestrel Vier."; 2.3, "Sie haben meine ID auf eine Drohne gespiegelt."
                                    5.4, "Flieg nicht mit meinem Namen nach Morgenlicht." |] } }
           { Port = "station-helios"; Find = Some 1
             Call = { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 13
                      Subject = "Orlov über die Schichtliste"
                      Captions = [| 0.0, "Ich hab die Seite mit den Namen ausgedruckt."; 3.5, "Die liegt bei mir im Schichtbüro,"
                                    5.0, "falls wieder jemand nach seiner Familie fragt." |] } }
           { Port = "station-kestrel"; Find = Some 2
             Call = { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 14
                      Subject = "Voss zur Aufnahme von Drift-9"
                      Captions = [| 0.0, "Das ist meine Tochter."; 3.0, "Ich hab mir die Stelle dreimal angehört"
                                    4.6, "und beim dritten Mal immer noch gewartet, dass sie den Funk wieder aufhebt." |] } }
           { Port = "station-aether"; Find = Some 4
             Call = { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 15
                      Subject = "Mara Lind zu den beiden Spuren"
                      Captions = [| 0.0, "Jetzt ergibt die Abrechnung im Lizenzordner Sinn."; 3.2, "AETHER hat für beide Sender nur eine Lizenz bezahlt."
                                    7.3, "Ich leg dir eine Kopie zum Band." |] } }
           { Port = "station-aether"; Find = None
             Call = { Portrait = "sera"; Name = "Sera"; Channel = "Relais Theta · RT-9-Nebenband"; Beat = 16
                      Subject = "Sera über deine Kennung"
                      Captions = [| 0.0, "Ich sammle die Puffer-Echos, damit kein Name gelöscht wird."
                                    3.4, "Deine Kennung gehörte einmal einem Kurier namens Halden." |] } } |]

    let hasSideband pilot = PilotSkills.level pilot 0 >= 1 && PilotSkills.contains pilot.Visited 4

    let archive pilot =
        recordings |> Array.filter (fun recording ->
            match recording.Find with
            | Some sector -> PilotSkills.contains pilot.Relics sector
            | None -> hasSideband pilot)
