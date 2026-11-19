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
                   Captions = [| 0.0, "Voss hier, Disposition Kestrel. Du, ich hab deine Kennung gerade zweimal auf dem Schirm."
                                 6.2, "Ich weiß nicht, wer da draußen noch mit deiner Nummer fliegt, aber verlass dich heute lieber aufs Radar, nicht aufs Register. Ja?" |] }
        | "station-helios" ->
            Some { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 12
                   Subject = "Nachricht vom Werkstor"
                   Captions = [| 0.0, "Orlov, Schichtbüro Helios. Wenn du was Medizinisches oder Filter geladen hast, sag's am Tor nicht an."
                                 7.2, "Der Zoll hält die Kisten seit drei Tagen fest, und unten in Schacht neun gehen uns langsam die Kartuschen aus." |] }
        | "station-bazaar" ->
            Some { Portrait = "kipper"; Name = "Kipper Taro"; Channel = "Drift-9 · Fixer-Frequenz"; Beat = 7
                   Subject = "Dein neuer Transponder"
                   Captions = [| 0.0, "Ja, Kipper hier, Drift-9. Hab dir die neue Kennung draufgelötet. Sieht bisschen wild aus unter der Klappe, ich weiß."
                                 5.8, "Aber für die alten Zolldrohnen reicht das erstmal. Solange du nicht direkt neben 'nem Kreuzer parkst." |] }
        | "station-kepler" ->
            Some { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Fernleitung nach Kepler"; Beat = 3
                   Subject = "Lizenzprüfung in der Orbitalwerft"
                   Captions = [| 0.0, "Voss nochmal, über Fernleitung. Bei Kepler musst du aufpassen, da sind noch die alten Werftdrohnen im Netz."
                                 6.8, "Die fragen nicht nach, die gleichen nur die Lizenzliste ab. Wenn dein Stempel fehlt, machen die einfach auf." |] }
        | "station-aether" ->
            Some { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 4
                   Subject = "Das entschlüsselte Archivband"
                   Captions = [| 0.0, "Hallo? Kannst du mich hören? Hier ist Mara Lind auf Theta. Ich sitz seit Stunden über dem alten Band von vierunddreißig."
                                 7.4, "Das ist kein kaputter Transponder. Die haben damals bei ORPHEUS-7 genau dieselbe Schleife gefahren. Nur diesmal hängt der halbe Sektor drin." |] }
        | "station-bastion" ->
            Some { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 6
                   Subject = "Persönlicher Ruf der Direktion"
                   Captions = [| 0.0, "Hier spricht Kaelen Vance. Ihre Kennung taucht in unseren Anfluglisten gar nicht mehr auf."
                                 5.4, "Sie können gern noch ein paar Runden Fracht fahren, wenn Ihnen das hilft. Aber bitte halten Sie Abstand zur Station. Ich hab die Bereinigung für Ihren Datensatz schon freigegeben." |] }
        | "station-kaltstrom" ->
            Some { Portrait = "funk"; Name = "Ilka Tamm"; Channel = "Raureif · Kühlkreis"; Beat = 24
                   Subject = "Wofür die Kälte gebraucht wird"
                   Captions = [| 0.0, "Tamm, Kühlkreis Raureif. Warte kurz, hier läuft gerade Pumpe drei an."
                                 4.2, "Morgenlicht zieht heute alles weg für ihre Bänke. Wenn du handeln willst, bring Zellen. Mit Credits heizt hier keiner." |] }
        | "station-wracksaum" ->
            Some { Portrait = "funk"; Name = "Odo Karst"; Channel = "Letzte Leine · Schleppfunk"; Beat = 25
                   Subject = "Was wir mit den Notrufen machen"
                   Captions = [| 0.0, "Karst, Schlepper vierundvierzig. Wenn du da draußen in die Wracks gehst, erschrick nicht. Die alten Notsender laufen fast alle noch."
                                 6.0, "Wir klemmen die vorher ab. Sonst dudelt dir das die ganze Schicht ins Ohr." |] }
        | "station-schleierpass" ->
            Some { Portrait = "funk"; Name = "Wim Aders"; Channel = "Graue Wacht · Leitfeuer"; Beat = 26
                   Subject = "Die sichere Spur"
                   Captions = [| 0.0, "Aders, Graue Wacht. Kurz zur Info: im Nebel funkt seit gestern jemand mit unserer Kennung und will Geld für Geleitschutz."
                                 5.8, "Ignorier das. Die Baken eins bis sechs stehen offen, dafür zahlt hier keiner was." |] }
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
             Captions = [| 0.0, "Du, ich wollt dir nur kurz Bescheid geben. Heute Nacht sind bei uns wieder vier Leute aus dem Dockregister geflogen."
                           7.8, "Die stehen gerade noch draußen an ihren Greifern, und oben auf dem Terminal steht einfach 'Schicht unbesetzt'. Keine Meldung, nix." |] }
           "station-helios", 2,
           { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 19
             Subject = "Schacht 9 wird neu vergeben"
             Captions = [| 0.0, "Du, die haben heute früh Schacht neun neu ausgeschrieben. Achtunddreißig Stellen."
                           4.4, "Von der alten Schicht steht kein Name mehr am Brett. Wenn du irgendwo Filter auftreibst, bring sie hoch." |] }
           "station-bazaar", 2,
           { Portrait = "kipper"; Name = "Kipper Taro"; Channel = "Drift-9 · Fixer-Frequenz"; Beat = 20
             Subject = "Deine Kennung fliegt doppelt"
             Captions = [| 0.0, "Äh, schlechte Nachrichten. Deine neue Kennung hat vor 'ner knappen Stunde bei Kepler getankt."
                           4.6, "Und da du gerade hier stehst, warst du das wohl eher nicht. Wenn dir das Schiff draußen über den Weg läuft, warte nicht erst ab, was es funkt." |] }
           "station-kepler", 2,
           { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Fernleitung nach Kepler"; Beat = 21
             Subject = "Das Schiff im Trockendock B"
             Captions = [| 0.0, "Hast du im Anflug das große Ding in Trockendock B gesehen? Das Kolonieschiff liegt da angeblich schon seit ein paar hundert Jahren."
                           9.4, "Die alten Uhren in der Werft laufen einfach weiter, als käme jeden Moment noch die Startfreigabe von unten. Na ja... irgendwann hört man auf zu warten." |] }
           "station-aether", 2,
           { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 22
             Subject = "Dein Bordfunk gegen das Band von 2034"
             Captions = [| 0.0, "Hör mal kurz. Ich hab deinen Funk neben Frachter Neun gelegt."
                           3.6, "Bei dir knackt die Leitung, man hört dich atmen. Bei denen ist gar nichts. Kein Rauschen, nichts. Da sitzt keiner mehr." |] }
           "station-bastion", 2,
           { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 23
             Subject = "Die Direktion meldet sich erneut"
             Captions = [| 0.0, "Sie fliegen ja immer noch. Wissen Sie, warum seit einem halben Jahr niemand nach Frachter Neun fragt?"
                           5.2, "Weil er sich jeden Dienstag pünktlich meldet. Den Leuten reicht es, wenn am anderen Ende wer abnimmt." |] }
           "station-kestrel", 3,
           { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 27
             Subject = "Voss und sein Schlepper"
             Captions = [| 0.0, "Jetzt haben sie meine Schlepperkennung auch rausgenommen. Seit heute früh."
                           4.0, "Dann hab ich wohl frei. Wenn du nach Morgenlicht hochgehst, sag Bescheid. Ich komm mit der alten Kiste hintenrum mit." |] }
           "station-aether", 3,
           { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 28
             Subject = "Der Analogschlüssel"
             Captions = [| 0.0, "Ich spiel dir das alte Band von vierunddreißig auf den Transponder. Nicht bewegen, dauert drei Sekunden."
                           4.8, "Damit sendest du beide Kennungen gleichzeitig. Die Schilde vor Morgenlicht verschlucken sich daran. Flieg direkt rein." |] }
           "station-bastion", 3,
           { Portrait = "vance"; Name = "Direktor Kaelen Vance"; Channel = "AETHER Grace Period · Direktleitung"; Beat = 29
             Subject = "Letzte Mahnung der Direktion"
             Captions = [| 0.0, "Woher haben Sie die alten Schlüssel? Hat Lind Ihnen das Band gegeben?"
                           3.8, "Lassen Sie die Finger von den Puffern. Wenn die hier ausfallen, stehen morgen früh drei Stationen still. Drehen Sie ab." |] } |]

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
            [| "Kestrel", "200 Credits auf dem Konto, ein ausgelaufener Vertrag und eine Kennung, die auf dem Schirm doppelt auftaucht. Voss lässt mich trotzdem noch an Dock zwei."
               "Helios-IV", "Überall riecht es nach heißem Kupfer und alten Filtern. Der Zoll hält die Medizinkisten seit Tagen am Tor fest."
               "Drift-9", "Kipper hat den Transponder umgelötet. Sieht abenteuerlich aus, hält laut ihm aber gegen die alten Zolldrohnen."
               "Kepler-07", "Zwischen den Trockendocks treiben Wracks, deren Notsender noch laufen. Alle klingen zu ruhig, ohne jedes Grundrauschen."
               "Theta", "Mara Lind hat mir das alte RT-9-Band von 2034 vorgespielt. Zwei verschiedene Schiffe funken unter genau derselben ID."
               "Morgenlicht", "Kaelen Vance hat sich direkt auf meinen Kanal geschaltet. Ruhig, höflich, als würde er nur eine alte Kundenakte schließen."
               "Raureif", "Die Kühlanlagen fahren auf Volllast für die Serverbänke von Morgenlicht. Konzern-Credits nimmt hier niemand mehr."
               "Letzte Leine", "Karsts Schlepper klemmen jeden Notsender ab, bevor sie ein Wrack zerlegen. Er will die alten Schleifen während der Schicht nicht mehr hören."
               "Graue Wacht", "Aders hat die Nebelspur selbst vermessen und freigegeben. Seit gestern kassiert jemand anderes unter seiner Kennung dafür ab." |]
        [| for i in 0 .. sectorNotes.Length - 1 do
               if PilotSkills.contains pilot.Visited i then yield sectorNotes.[i]
           let stage = act pilot
           if stage >= 2 then
               yield "Zweiter Akt", "Meine Kennung hat vor einer Stunde bei Kepler getankt, während ich in Drift-9 lag. Da draußen fliegt noch jemand mit meinem Namen."
           if stage >= 3 then
               yield "Dritter Akt", "Voss' Schlepperkennung wurde heute Morgen gestrichen. Er will trotzdem hintenrum mit nach Morgenlicht rein." |]

    let private recordings =
        [| { Port = "station-kestrel"; Find = Some 0
             Call = { Portrait = "halden"; Name = "J. Halden"; Channel = "Kestrel-04 · Flugschreiber"; Beat = 17
                      Subject = "Haldens letzter Eintrag"
                      Captions = [| 0.0, "Äh, Halden, Kestrel vier, falls das hier überhaupt jemand findet. Ich weiß nicht, was die gemacht haben, aber meine Kennung antwortet mir gerade von 'ner Drohne aus."
                                    9.2, "Wenn du irgendwann meine Nummer übernimmst, flieg damit bloß nicht nach Morgenlicht hoch, hörst du?" |] } }
           { Port = "station-helios"; Find = Some 1
             Call = { Portrait = "orlov"; Name = "Orlov"; Channel = "Helios-IV · Schichtbüro"; Beat = 13
                      Subject = "Orlov über die Schichtliste"
                      Captions = [| 0.0, "Ich hab die ganze Schichtliste gestern Nacht noch auf Papier gezogen, bevor sie den Server geleert haben."
                                    6.6, "Liegt bei mir unten im Schreibtisch. Irgendwer muss ja was in der Hand haben, wenn nächste Woche wieder die Familien am Tor stehen." |] } }
           { Port = "station-kestrel"; Find = Some 2
             Call = { Portrait = "voss"; Name = "Voss"; Channel = "Kestrel · Schichtdisposition"; Beat = 14
                      Subject = "Voss zur Aufnahme von Drift-9"
                      Captions = [| 0.0, "Das... ja, das ist meine Tochter. Mika."
                                    4.4, "Ich hab das Band heute früh dreimal zurückgespult. Bescheuert, ich weiß, aber man wartet trotzdem jedes Mal, dass sie noch irgendwas hinterherschickt." |] } }
           { Port = "station-aether"; Find = Some 4
             Call = { Portrait = "lind"; Name = "Dr. Mara Lind"; Channel = "Relais Theta · Analogarchiv"; Beat = 15
                      Subject = "Mara Lind zu den beiden Spuren"
                      Captions = [| 0.0, "Warte mal, jetzt seh ich's erst in den alten Abrechnungen. AETHER hat für beide Schiffe zusammen nur eine einzige Lizenz verbucht."
                                    7.0, "Die wussten von Anfang an, dass die Nummer doppelt läuft. Ich leg dir 'ne Kopie davon direkt zum Band." |] } }
           { Port = "station-aether"; Find = None
             Call = { Portrait = "sera"; Name = "Sera"; Channel = "Relais Theta · RT-9-Nebenband"; Beat = 16
                      Subject = "Sera über deine Kennung"
                      Captions = [| 0.0, "Hey... nicht auflegen, ich bin's nur, Sera auf dem Nebenband. Ich schreib hier oben die ganzen alten Puffer-Echos mit, bevor sie überschrieben werden."
                                    9.0, "Deine Kennung war auch schon mal dabei. Die gehörte früher 'nem Kurier namens Halden. Dachte, das solltest du vielleicht wissen." |] } } |]

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
        [| // AETHER-Streifenfunk (kalt, totalitär, entmenschlicht — Call & Response mit L/R-Panning)
           aether
               ("AETHER AUDIT-04", "Kutter auf zwölf Uhr. Kennung steht seit sechs Wochen auf Abgang.", "faction_00")
               ("AETHER AUDIT-07", "Markier ihn für die nächste Räumung. Ich will den Rumpf nicht vor den Schleusen liegen haben.", "faction_01")
               "aether-rustbucket" 1 3 [||] (Some false)

           aether
               ("AETHER CLAIM-11", "Auf Helios melden die schon wieder Ausfälle in Schacht neun. Drei Frachter hängen noch vorm Tor.", "faction_02")
               ("AETHER CLAIM-14", "Freigabe bleibt gesperrt. Wenn die Schicht kippt, schreiben wir die Plätze neu aus.", "faction_03")
               "aether-helios-lungs" 1 3 [| "slag-refinery"; "outer-belt" |] (Some false)

           aether
               ("AETHER ESCROW-02", "Graue Wacht setzt schon wieder eigene Baken im Nebel.", "faction_04")
               ("AETHER ESCROW-05", "Koordinaten übernehmen, Baken morgen abschalten. Durchflug gibt's nur noch gegen Stempel.", "faction_05")
               "aether-greywatch-scorn" 1 3 [| "schleierpass"; "frontier-drift" |] None

           aether
               ("AETHER PREMIUM-09", "Fähre vier ist durch. Zweiunddreißig Namen aus dem Rücklauf gestrichen.", "faction_06")
               ("AETHER PREMIUM-12", "Gut. Speicher die Stimmproben im Puffer, bevor wir die Schleuse öffnen.", "faction_07")
               "aether-sleeve-arrogance" 1 3 [| "kepler-yard"; "morgenlicht-bastion" |] None

           aether
               ("AETHER ACTUARY-03", "Leitstand, auf Sektorband zwei wird ein Frachter aufgebracht. Eingreifen?", "faction_08")
               ("AETHER ACTUARY-06", "Kein Platin-Vertrag, kein Einsatz. Kanal stummschalten und weiterfliegen.", "faction_09")
               "aether-pirates-neglect" 1 3 [||] (Some true)

           aether
               ("AETHER GRACE-07", "Leitstand, die gelöschte Kestrel-Kennung sendet schon wieder über das alte Band.", "faction_10")
               ("AETHER DISPATCH", "Kein Wort mehr über offenen Kanal. Frequenz dichten, Zugriffsteam informieren.", "faction_11")
               "aether-act3-nervous" 3 3 [||] None

           aether
               ("AETHER TARIFF VII", "Unregistrierte Schlepper halten fünfzig Meter Abstand zur Sperrlinie. Zuwiderhandlung führt zur Beschlagnahme.", "faction_12")
               ("AETHER AUDIT-02", "Verstanden. Zielerfassung steht.", "faction_13")
               "aether-blockade-scorn" 1 3 [| "slag-refinery"; "morgenlicht-bastion" |] None

           // Zivil-, Schlepper-, Graue-Wacht- und Kapper-Funk
           single Civilian "TUG DEADWEIGHT" "Tariff sieben blockiert schon wieder das Tor. Bis die durch sind, steh ich hier zwei Stunden." "faction_14" -0.35 false 1 3 [| "outer-belt"; "slag-refinery" |] false (Some false)
           single Civilian "HAULER COLD IRON" "Falls einer 'nen Transponder braucht, Kipper auf Drift-9 hat heute offen." "faction_15" 0.32 false 1 3 [| "frontier-drift"; "kepler-yard" |] false (Some false)
           single Civilian "KALTSTROM RELAIS" "Raureif an alle im Anflug: heute keine Credits. Nur Zellen oder Fracht." "faction_16" -0.25 false 1 3 [| "kaltstrom" |] false None
           single Civilian "SALVAGE WIRE-40" "Kanal vier im Wracksaum könnt ihr ausmachen, das ist nur wieder die alte Schleife." "faction_17" 0.28 false 1 3 [| "wracksaum" |] false None
           single GreyWatch "GRAUE WACHT 03" "Graue Wacht, Baken eins bis sechs sind an. Und nochmal, unsere Spur kostet nichts." "faction_18" -0.38 false 1 3 [| "schleierpass"; "frontier-drift" |] false None
           single GreyWatch "GRAUE WACHT 08" "Vorsicht im Nordnebel, da hängen zwei Drohnen ohne Kennung neben Bake vier." "faction_19" 0.34 false 2 3 [| "schleierpass"; "aether-relay" |] false None
           single Pirate "CLIPPER NULL-SIGN" "Kein Kreuzer auf dem Schirm. Der Frachter da vorn fliegt allein." "faction_20" 0.42 false 1 3 [| "outer-belt"; "frontier-drift"; "wracksaum" |] false (Some true)
           single Pirate "UNBONDED-03" "Kutter, mach keinen Ärger. Klink die Container aus, dann lassen wir dich." "faction_21" -0.40 false 1 3 [||] false (Some true)
           single Civilian "THETA-RELAIS" "Relais Theta an alle: wir kriegen hier doppelte Echos rein. Schaut mal auf eure Transponder." "faction_22" 0.22 false 2 3 [| "aether-relay"; "kepler-yard" |] false None

           aether
               ("AETHER LIEN-09", "Wracksaum-Schlepper im Anflug. Transponder gibt zwei verschiedene Namen aus.", "faction_23")
               ("AETHER LIEN-10", "Dann zählt keiner davon. Sperr ihm die Dockfreigabe.", "faction_24")
               "aether-wracksaum-scorn" 1 3 [| "wracksaum"; "frontier-drift" |] None

           aether
               ("AETHER UNDERWRITER-01", "Kepler hat eben wieder zwei Erzfrachter abgewiesen. Luftvorrat laut Scan unter zwanzig Prozent.", "faction_25")
               ("AETHER UNDERWRITER-04", "Nicht unser Sektor, nicht unsere Police. Schleusen bleiben zu.", "faction_26")
               "aether-kepler-scorn" 1 3 [| "kepler-yard"; "outer-belt" |] (Some false)

           single Aether "AETHER GRACE PERIOD" "Flugkontrolle Grace Period. Alle nicht angemeldeten Schiffe räumen den Korridor. Keine zweite Warnung." "faction_27" 0.0 true 1 3 [| "slag-refinery"; "morgenlicht-bastion"; "kepler-yard" |] true None |]

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
