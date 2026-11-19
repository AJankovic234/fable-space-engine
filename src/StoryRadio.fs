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
    // Beats 0..17 have recorded voices; 18..29 arrive as text until they are recorded.
    let heardMask = (1 <<< 30) - 1

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
            Some { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 6
                   Subject = "Persönlicher Ruf der Direktion"
                   Captions = [| 0.0, "Oh, niedlich."; 1.4, "Ein rostiger Erzfrachter spielt Händler."
                                 4.3, "Genieß deine Credits, Kleiner."; 6.2, "Ich lösch dich gleich aus dem System." |] }
        | "station-kaltstrom" ->
            Some { Portrait = "funk"; Name = "Ilka Tamm"; Channel = "Raureif · Kühlkreis"; Beat = 24
                   Subject = "Wofür die Kälte gebraucht wird"
                   Captions = [| 0.0, "Raureif, Kühlkreis. Tamm am Rohr."
                                 2.6, "Wir brennen Kälte für Morgenlicht. Die Pufferbänke laufen heiß, wenn so viele Tote weitersenden."
                                 8.4, "Zahl in Zellen. Credits frieren hier nicht." |] }
        | "station-wracksaum" ->
            Some { Portrait = "funk"; Name = "Odo Karst"; Channel = "Letzte Leine · Schleppfunk"; Beat = 25
                   Subject = "Was wir mit den Notrufen machen"
                   Captions = [| 0.0, "Karst, Schlepper Vierundvierzig."
                                 2.4, "Jedes Wrack hier funkt noch. Wir kappen den Sender, bevor wir die Presse bergen."
                                 7.6, "Sera nennt das Löschen. Ich nenn es Feierabend." |] }
        | "station-schleierpass" ->
            Some { Portrait = "funk"; Name = "Wim Aders"; Channel = "Graue Wacht · Leitfeuer"; Beat = 26
                   Subject = "Die sichere Spur"
                   Captions = [| 0.0, "Graue Wacht, Aders."
                                 2.0, "Wir haben die Spur durch den Nebel vermessen und verschenkt."
                                 6.1, "Seit gestern verkauft sie jemand als Geleitschutz. Unter unserer Kennung." |] }
        | _ -> None

    /// Akt der Geschichte aus dem Fortschritt des Piloten: Jeder besuchte Sektor zählt einfach,
    /// jeder Fund doppelt. Akt III beginnt erst, wenn der Pilot Morgenlicht gesehen hat.
    let act (pilot: PilotProgress) =
        let progress = PilotSkills.count pilot.Visited + 2 * PilotSkills.count pilot.Relics
        if progress >= 13 && PilotSkills.contains pilot.Visited 5 then 3
        elif progress >= 6 then 2
        else 1

    // Spätere Rufe derselben Station: (Station, Akt, Ruf). Sie lösen den ersten Ruf ab.
    let private later =
        [| "station-kestrel", 2,
           { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 18
             Subject = "Die gestrichene Schicht"
             Captions = [| 0.0, "Vance hat heute Nacht vier Kennungen aus dem Dockregister gestrichen."
                           4.6, "Die Leute stehen noch an ihren Greifern."
                           7.2, "Laut Liste war die Schicht nie besetzt." |] }
           "station-helios", 2,
           { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 19
             Subject = "Schacht 9 wird neu vergeben"
             Captions = [| 0.0, "Sie haben Schacht 9 neu ausgeschrieben."
                           3.0, "38 Stellen. Vorbesitzer unbekannt."
                           6.0, "Bring mir keine Blumen. Bring Filter." |] }
           "station-bazaar", 2,
           { Portrait = "kipper"; Name = "Kipper Taro"; Channel = "Drift-9 · Fixer-Frequenz"; Beat = 20
             Subject = "Deine Kennung fliegt doppelt"
             Captions = [| 0.0, "Schlechte Nachricht, Lizenzleiche."
                           2.2, "Deine Kennung hat vor einer Stunde bei Kepler getankt. Du warst nicht dabei."
                           7.0, "Wenn dir das Ding begegnet: zuerst schießen, dann rechnen." |] }
           "station-kepler", 2,
           { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Fernleitung nach Kepler"; Beat = 21
             Subject = "Das Schiff im Trockendock B"
             Captions = [| 0.0, "Im Trockendock B liegt ein Kolonieschiff, das seit sechshundert Jahren auf Startfreigabe wartet."
                           6.2, "Die Werft zählt seitdem ihre eigenen Jahre."
                           9.0, "Hier draußen wartet jeder auf eine Bodenstation, die nicht mehr antwortet." |] }
           "station-aether", 2,
           { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 22
             Subject = "Dein Bordfunk gegen das Band von 2034"
             Captions = [| 0.0, "Ich habe deinen Bordfunk gegen das Band von 2034 gelegt."
                           3.8, "Deine Stimme rauscht. Die von Frachter Neun nicht mehr."
                           7.4, "Vertrau keiner Stimme ohne Rauschen. Rauschen heißt, da atmet noch jemand." |] }
           "station-bastion", 2,
           { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 23
             Subject = "Die Direktion meldet sich erneut"
             Captions = [| 0.0, "Du schon wieder."
                           1.6, "Weißt du, warum niemand nach Frachter Neun sucht?"
                           4.8, "Mein Puffer funkt höflicher als ein erstickender Schürfer."
                           8.4, "Du wirst ein hervorragender Puffer." |] }
           "station-kestrel", 3,
           { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 27
             Subject = "Voss und sein Schlepper"
             Captions = [| 0.0, "Vance hat meine Schlepperkennung gestrichen."
                           3.1, "Laut Register gibt es mich seit heute früh nicht mehr."
                           6.4, "Gut. Dann sieht mich auch keiner kommen, Partner." |] }
           "station-aether", 3,
           { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 28
             Subject = "Der Analogschlüssel"
             Captions = [| 0.0, "Ich speise das Band von 2034 auf deinen Transponder."
                           3.6, "Zwei Körper, eine Kennung. Sein Schild kann das nicht auseinanderhalten."
                           8.0, "Flieg, solange es rauscht." |] }
           "station-bastion", 3,
           { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 29
             Subject = "Letzte Mahnung der Direktion"
             Captions = [| 0.0, "Wer hat euch Analogsäcken die RT-9-Schlüssel gegeben?"
                           3.4, "Ich halte diesen ganzen verdammten Sektor zusammen."
                           6.6, "Sleeve Nummer sieben wird sich an dich erinnern. Nummer acht auch." |] } |]

    /// Alle für diesen Piloten freigeschalteten Rufe einer Station, ältester zuerst.
    let calls pilot port =
        let stage = act pilot
        [| yield! Option.toArray (atPort port)
           for station, needed, call in later do
               if station = port && needed <= stage then yield call |]

    /// Der aktuelle Ruf der Station: der jüngste, den der Akt freigibt.
    let contact pilot port = calls pilot port |> Array.tryLast

    /// Frühere Rufe der Station, die der aktuelle abgelöst hat.
    let earlier pilot port =
        let all = calls pilot port
        if all.Length > 1 then all.[.. all.Length - 2] else Array.empty

    /// Logbuch des Piloten: ein Eintrag je besuchtem Sektor, dazu einer je Akt.
    let logbook (pilot: PilotProgress) =
        let sectorNotes =
            [| "Kestrel", "200 Credits, ein geliehener Sleeve-Vertrag und eine tote Kennung. Kestrel lässt mich noch andocken."
               "Helios-IV", "In den Schmelzöfen riecht die Luft nach Kupfer und verbranntem Filter. Medizin kostet hier mehr als ein ganzes Leben."
               "Drift-9", "Kipper hat meinen Transponder neu gelötet. »Hält bis zur nächsten Patrouille«, sagt er, »danach bist du offiziell wieder tot.«"
               "Kepler-07", "Zwischen den Trockendocks treiben Wracks mit laufenden Notrufen. Alle klingen gleich: sauber, ruhig, synthetisch."
               "Theta", "Mara Lind hat mir das RT-9-Band von 2034 vorgespielt. Zwei Körper funken unter derselben ID."
               "Morgenlicht", "Vance hat sich direkt in meinen Bordfunk geschaltet. Für ihn bin ich kein Pilot, sondern eine unversicherte Abschreibung."
               "Raureif", "Die Destille verkauft Kälte an Morgenlicht, damit die Pufferbänke nicht durchbrennen. Hier draußen kühlen die Lebenden die Toten."
               "Letzte Leine", "Karsts Leute kappen jeden Notruf, bevor sie ein Wrack ausschlachten. Ich habe nicht gefragt, wie viele Stimmen das diese Woche waren."
               "Graue Wacht", "Aders zeigt mir die Spur durch den Nebel auf Papier. Auf dem Schirm verkauft sie jemand anderes, unter seiner Kennung." |]
        [| for i in 0 .. sectorNotes.Length - 1 do
               if PilotSkills.contains pilot.Visited i then yield sectorNotes.[i]
           let stage = act pilot
           if stage >= 2 then
               yield "Zweiter Akt", "Meine Kennung hat bei Kepler getankt, während ich bei Kestrel lag. Irgendwo fliegt noch ein Schiff mit meinem Namen."
           if stage >= 3 then
               yield "Dritter Akt", "Vance hat die Kennung von Voss’ Schlepper gestrichen. Der alte Mann fährt trotzdem in den Sperrkorridor." |]

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

type RadioFaction =
    | Aether
    | Civilian
    | Pirate
    | GreyWatch

type RadioLine =
    { Faction: RadioFaction
      Callsign: string
      Text: string
      AudioKey: string
      Pan: float
      CleanFilter: bool }

type RadioExchange =
    { Id: string
      Lines: RadioLine[]
      MinAct: int
      MaxAct: int
      Sectors: string[]
      RequiresAetherNear: bool
      RequiresCombat: bool option }

type RadioContext =
    { SectorId: string
      Act: int
      AetherNear: bool
      CombatActive: bool }

[<RequireQualifiedAccess>]
module RadioChatter =
    let factionTag = function
        | Aether -> 0
        | Civilian -> 1
        | Pirate -> 2
        | GreyWatch -> 3

    let private aether left right id minAct maxAct sectors combat =
        let lCall, lText, lKey = left
        let rCall, rText, rKey = right
        { Id = id
          MinAct = minAct
          MaxAct = maxAct
          Sectors = sectors
          RequiresAetherNear = true
          RequiresCombat = combat
          Lines =
            [| { Faction = Aether; Callsign = lCall; Text = lText; AudioKey = lKey; Pan = -0.45; CleanFilter = true }
               { Faction = Aether; Callsign = rCall; Text = rText; AudioKey = rKey; Pan = 0.45; CleanFilter = true } |] }

    let private single faction callsign (text: string) audioKey pan clean minAct maxAct sectors aetherNear combat =
        { Id = callsign + ":" + audioKey + ":" + text.Substring(0, min 12 text.Length)
          MinAct = minAct
          MaxAct = maxAct
          Sectors = sectors
          RequiresAetherNear = aetherNear
          RequiresCombat = combat
          Lines =
            [| { Faction = faction; Callsign = callsign; Text = text; AudioKey = audioKey; Pan = pan; CleanFilter = clean } |] }

    let exchanges : RadioExchange[] =
        [| // Abwertende AETHER-Zweierdialoge (Call & Response mit L/R-Panning)
           aether
               ("AETHER AUDIT-04", "Schon wieder ein Schürfer mit geflicktem Transponder. Riecht bis hierher nach billigem Kühlmittel.", "faction_00")
               ("AETHER AUDIT-07", "Lass den Kutter durch. Wenn sein Stack reißt, spart das wenigstens die Hafensteuer.", "faction_01")
               "aether-rustbucket" 1 3 [||] (Some false)

           aether
               ("AETHER CLAIM-11", "Schichtwechsel auf Helios. Dreiundzwanzig unversicherte Lungen mehr auf der Liste.", "faction_02")
               ("AETHER CLAIM-14", "Solange der Schmelzofen brennt, zahlt die Konzernkasse keinen einzigen Filter.", "faction_03")
               "aether-helios-lungs" 1 3 [| "slag-refinery"; "outer-belt" |] (Some false)

           aether
               ("AETHER ESCROW-02", "Die Graue Wacht markiert wieder Nebelspuren für umsonst. Unzertifizierte Amateure.", "faction_04")
               ("AETHER ESCROW-05", "Ab morgen verkaufen wir dieselbe Spur als Platin-Geleitschutz. Mit Stempel.", "faction_05")
               "aether-greywatch-scorn" 1 3 [| "schleierpass"; "frontier-drift" |] None

           aether
               ("AETHER PREMIUM-09", "Mein letzter Sleeve hatte einen Kratzer am Handgelenk. Hab ihn auf Morgenlicht tauschen lassen.", "faction_06")
               ("AETHER PREMIUM-12", "Dekadent. Meine Police deckt erst ab Totalverlust – oder Gürtelkontakt.", "faction_07")
               "aether-sleeve-arrogance" 1 3 [| "kepler-yard"; "morgenlicht-bastion" |] None

           aether
               ("AETHER ACTUARY-03", "Kapper auf Sektorband. Sollen die Drohnen eingreifen?", "faction_08")
               ("AETHER ACTUARY-06", "Negativ. Solange sie nur Gürtelfrachter plündern, sind das unbezahlte Marktregulatoren.", "faction_09")
               "aether-pirates-neglect" 1 3 [||] (Some true)

           aether
               ("AETHER GRACE-07", "Leitstand, die gestrichene Kestrel-Kennung funkt schon wieder auf dem Nebenband.", "faction_10")
               ("AETHER DISPATCH", "Ignorieren. Und erwähnen Sie das Analogband nicht noch einmal – die Direktion hört mit.", "faction_11")
               "aether-act3-nervous" 3 3 [||] None

           aether
               ("AETHER TARIFF VII", "Schlepper ohne Platin-Bürgschaft halten fünfzig Meter Abstand zur Sperrlinie.", "faction_12")
               ("AETHER AUDIT-02", "Verstanden. Wer die Linie kratzt, wird als Schrottgebühr verbucht.", "faction_13")
               "aether-blockade-scorn" 1 3 [| "slag-refinery"; "morgenlicht-bastion" |] None

           // Zivil-, Schlepper-, Graue-Wacht- und Kapper-Funk
           single Civilian "TUG DEADWEIGHT" "AETHER Tariff VII steht wieder quer vor dem Tor. Für jeden Scan verlierst du zwei Stunden Sauerstoff." "faction_14" -0.35 false 1 3 [| "outer-belt"; "slag-refinery" |] false (Some false)
           single Civilian "HAULER COLD IRON" "Auf Drift-9 löten sie dir eine neue SIN aus Altmetall. Hält genau bis zur ersten Drohne." "faction_15" 0.32 false 1 3 [| "frontier-drift"; "kepler-yard" |] false (Some false)
           single Civilian "KALTSTROM RELAIS" "Zahlen in Zellen, nicht in Credits. Auf Raureif taut dir kein Konzernscheck den Verdampfer auf." "faction_16" -0.25 false 1 3 [| "kaltstrom" |] false None
           single Civilian "SALVAGE WIRE-40" "Wenn dir im Wracksaum ein Notruf zu sauber klingt, kapp die Leitung. Tote atmen nicht so ruhig." "faction_17" 0.28 false 1 3 [| "wracksaum" |] false None
           single GreyWatch "GRAUE WACHT 03" "Baken sind gesetzt. Lasst euch am Schleierpass kein AETHER-Geleit aufschwatzen – die Spur ist unsere." "faction_18" -0.38 false 1 3 [| "schleierpass"; "frontier-drift" |] false None
           single GreyWatch "GRAUE WACHT 08" "Achtung im Nebel: Zwei Drohnen ohne Transponder patrouillieren abseits der Baken." "faction_19" 0.34 false 2 3 [| "schleierpass"; "aether-relay" |] false None
           single Pirate "CLIPPER NULL-SIGN" "Kein AETHER-Stern in Reichweite. Der Erzfrachter da drüben gehört schon zur Hälfte uns." "faction_20" 0.42 false 1 3 [| "outer-belt"; "frontier-drift"; "wracksaum" |] false (Some true)
           single Pirate "UNBONDED-03" "Wirf die Ladung ab, Lizenzleiche, dann lassen wir dir genug Treibstoff bis Kestrel." "faction_21" -0.40 false 1 3 [||] false (Some true)
           single Civilian "THETA-RELAIS" "Analogband RT-9 meldet zwei identische Kennungen im selben Sektor. Prüft eure Transponder." "faction_22" 0.22 false 2 3 [| "aether-relay"; "kepler-yard" |] false None

           aether
               ("AETHER LIEN-09", "Warum riecht jeder Schlepper aus dem Wracksaum nach verbranntem Isolierband?", "faction_23")
               ("AETHER LIEN-10", "Weil ihre SIN-Lizenzen genauso geflickt sind wie ihre Hüllen.", "faction_24")
               "aether-wracksaum-scorn" 1 3 [| "wracksaum"; "frontier-drift" |] None

           aether
               ("AETHER UNDERWRITER-01", "Kepler hat wieder einen Erzfrachter ohne Platin-Stempel abgewiesen.", "faction_25")
               ("AETHER UNDERWRITER-04", "Gut so. Sollen sie ihr Altmetall draußen im Gürtel rosten lassen.", "faction_26")
               "aether-kepler-scorn" 1 3 [| "kepler-yard"; "outer-belt" |] (Some false)

           single Aether "AETHER GRACE PERIOD" "Unzertifizierte Einheiten räumen den Anflugkorridor. Schäden an Lackierungen werden in Rechnung gestellt." "faction_27" 0.0 true 1 3 [| "slag-refinery"; "morgenlicht-bastion"; "kepler-yard" |] true None |]

    let matches (ctx: RadioContext) (ex: RadioExchange) =
        ctx.Act >= ex.MinAct
        && ctx.Act <= ex.MaxAct
        && (not ex.RequiresAetherNear || ctx.AetherNear)
        && (ex.Sectors.Length = 0 || Array.contains ctx.SectorId ex.Sectors)
        && (match ex.RequiresCombat with
            | Some needed -> needed = ctx.CombatActive
            | None -> true)

    let pick (ctx: RadioContext) (recentIds: string[]) (roll: float) : RadioExchange option =
        let matching = exchanges |> Array.filter (matches ctx)
        if matching.Length = 0 then None
        else
            let fresh = matching |> Array.filter (fun ex -> not (Array.contains ex.Id recentIds))
            let pool = if fresh.Length > 0 then fresh else matching
            let clamped = max 0.0 (min 0.999999 roll)
            let idx = int (clamped * float pool.Length)
            Some pool.[idx]
