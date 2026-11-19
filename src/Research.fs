namespace SpaceEngine

open Fable.Core

type ResearchState =
    { mutable LabInstalled: bool
      mutable Unlocked: int }

type ResearchNode =
    { Id: int
      Branch: int
      Tier: int
      TierLabel: string
      Name: string
      /// Von wem oder woher das Wissen stammt (Figur, Station, Register).
      Source: string
      Summary: string
      /// Bandfragment, das das RT-9-Labor erst nach dem Einbau lesbar macht.
      Log: string
      Credits: int
      Materials: (CommodityId * int)[]
      Requires: int[]
      /// Sektoren, an denen der Einbau möglich ist.
      Ports: string[]
      /// Index des Fundes im Fundarchiv, der den Knoten entsiegelt; -1 = offen.
      Relic: int }

[<RequireQualifiedAccess>]
module Research =
    // Zweig 0: Werkstattbuch Jahr Null (Waffen)
    [<Literal>]
    let HighEnergyOptics = 0
    [<Literal>]
    let CryoDisruptor = 1
    [<Literal>]
    let CapacitorCooling = 2
    [<Literal>]
    let PhaseRailgun = 3
    [<Literal>]
    let SwarmMissiles = 4

    // Zweig 1: Stille Bänder (Felder & Schilde)
    [<Literal>]
    let ResonanceCoils = 5
    [<Literal>]
    let ShieldSiphon = 6
    [<Literal>]
    let CompositeArmour = 7
    [<Literal>]
    let PhaseReflector = 8
    [<Literal>]
    let AetherOvercharge = 9

    // Zweig 2: Lizenzleichen-Logistik (Fracht & Bergung)
    [<Literal>]
    let AiCargoRouting = 10
    [<Literal>]
    let QuantumHold = 11
    [<Literal>]
    let DeepSensors = 12
    [<Literal>]
    let TractorField = 13
    [<Literal>]
    let TradeMatrix = 14

    let branchNames =
        [| "Werkstattbuch Jahr Null"
           "Stille Bänder"
           "Gürtel-Praxis" |]

    let branchSources =
        [| "Waffen · Wissen der Orbitalwerft Kepler-07"
           "Felder & Schilde · Analogbänder von Relais Theta"
           "Fracht & Bergung · Werkstattkniffe aus dem Gürtel" |]

    let labSectorId = "aether-relay"
    let labCredits = 900
    let labMaterials = [| CommodityId.NavData, 3; CommodityId.PlasmaCores, 2 |]

    let private theta = "aether-relay"
    let private kepler = "kepler-yard"
    let private drift = "frontier-drift"
    let private hubs = [| theta; kepler |]

    let portName (sectorId: string) =
        if sectorId = theta then "Relais Theta"
        elif sectorId = kepler then "Kepler-07"
        elif sectorId = drift then "Drift-9"
        else sectorId

    let portList (ports: string[]) = ports |> Array.map portName |> String.concat " / "

    /// Namen der Funde, die Apex-Knoten entsiegeln (Index = Sektor im Fundarchiv).
    let relicName (relic: int) =
        match relic with
        | 3 -> "Trockendock B (Jahr Null)"
        | 5 -> "Sleeve-Satellit (Sperrzone Morgenlicht)"
        | 8 -> "Kursprotokoll 17 (Schleierpass)"
        | _ -> "Archivfund"

    /// Das Bord-Labor selbst gibt es nur bei Dr. Mara Lind auf Forschungs-Relais Theta.
    let isLabPort (sectorId: string) = sectorId = labSectorId

    let nodes : ResearchNode[] =
        [| // --- Zweig 0: Werkstattbuch Jahr Null (Waffen) ---
           { Id = HighEnergyOptics; Branch = 0; Tier = 1; TierLabel = "GRUNDLAGE"
             Name = "Trockendock-Optik"
             Source = "Werkstattbuch Kepler-07"
             Summary = "+8 % Laserschaden für alle Schiffe. Eingelagerte Linsensätze aus Trockendock B."
             Log = "WERKSTATTBUCH 03 AE: Linsensatz B abgenommen, Einbau nach Startfreigabe Erde. Die Zeile darunter ist leer geblieben."
             Credits = 350
             Materials = [| CommodityId.CryoOre, 3; CommodityId.TitaniumScrap, 2 |]
             Requires = [||]; Ports = hubs; Relic = -1 }
           { Id = CryoDisruptor; Branch = 0; Tier = 2; TierLabel = "PROTOTYP"
             Name = "Kryo-Disruptor"
             Source = "Ilka Tamm · Destille Raureif"
             Summary = "Schaltet Waffen-Kit „Kryo-Disruptor“ frei: unterkühlte Treffer bremsen das Ziel ab und verzögern seine Schildladung."
             Log = "Notiz von Ilka Tamm: Aus dem alten Raureif-Kreislauf ausgebaut. Die Düse klemmt ab und zu bei Dauerfeuer, aber es hält."
             Credits = 750
             Materials = [| CommodityId.CryoOre, 6; CommodityId.NavData, 3 |]
             Requires = [| HighEnergyOptics |]; Ports = hubs; Relic = -1 }
           { Id = CapacitorCooling; Branch = 0; Tier = 3; TierLabel = "SPEZIALISIERUNG"
             Name = "Null-Takt-Kühlung"
             Source = "Orlov · Helios-IV"
             Summary = "+12 % Feuerrate auf alle Bordwaffen und −15 % Nachladezeit für Abfangraketen."
             Log = "Orlov, Schacht 9: Wir haben die Rücklaufleitung einfach doppelt gelegt. Wird warm unterm Sitz, läuft aber sauber durch."
             Credits = 950
             Materials = [| CommodityId.CryoOre, 4; CommodityId.FuelCells, 3; CommodityId.PlasmaCores, 2 |]
             Requires = [| CryoDisruptor |]; Ports = hubs; Relic = -1 }
           { Id = PhaseRailgun; Branch = 0; Tier = 4; TierLabel = "DURCHBRUCH"
             Name = "Phasen-Railgun"
             Source = "Drohnenwerft · nur Kepler-07"
             Summary = "Schaltet Waffen-Kit „Phasen-Railgun“ frei: 2× Projektiltempo, +115 % Schaden und panzerbrechende Wirkung. Geborgene Schiene einer Abfangdrohne."
             Log = "WERFTPROTOKOLL KEPLER: Beschleunigerschiene aus geborgener Continuity-Drohne. Prüfplakette abgelaufen, Führungsnuten noch maßhaltig."
             Credits = 1400
             Materials = [| CommodityId.PlasmaCores, 4; CommodityId.TitaniumScrap, 5; CommodityId.NavData, 3 |]
             Requires = [| CapacitorCooling |]; Ports = [| kepler |]; Relic = -1 }
           { Id = SwarmMissiles; Branch = 0; Tier = 5; TierLabel = "APEX"
             Name = "Single-Seat-Schwarm"
             Source = "Trockendock B · nur Kepler-07"
             Summary = "J feuert eine 3er-Salve wendiger Lenkraketen gleichzeitig, die sich auf nahe Ziele aufteilen."
             Log = "WERKSTATTBUCH 03 AE, Randnotiz: Alle drei Suchköpfe senden mit derselben Kennung. Das Leitsystem bucht nur einen Flugkörper."
             Credits = 2200
             Materials = [| CommodityId.PlasmaCores, 6; CommodityId.NavData, 5; CommodityId.FuelCells, 6 |]
             Requires = [| PhaseRailgun; DeepSensors |]; Ports = [| kepler |]; Relic = 3 }

           // --- Zweig 1: Stille Bänder (Felder & Schilde) ---
           { Id = ResonanceCoils; Branch = 1; Tier = 1; TierLabel = "GRUNDLAGE"
             Name = "RT-9-Resonanzspulen"
             Source = "Dr. Mara Lind · Relais Theta"
             Summary = "+20 Schildkapazität für alle Schiffe. Analog gewickelt, ohne AETHER-Gateway dazwischen."
             Log = "Mara Lind: Von Hand gewickelt, ohne Gateway-Chip. Brummt ein bisschen auf dem Bordfunk, läuft dafür komplett autark."
             Credits = 350
             Materials = [| CommodityId.TitaniumScrap, 3; CommodityId.MedSupplies, 2 |]
             Requires = [||]; Ports = hubs; Relic = -1 }
           { Id = ShieldSiphon; Branch = 1; Tier = 2; TierLabel = "PROTOTYP"
             Name = "Puffer-Siphon"
             Source = "Sera · Relais Theta"
             Summary = "Jeder eigene Treffer auf einen Gegner lädt +3 Schildpunkte direkt nach."
             Log = "Sera: Die alten Relais ziehen bei jedem Impuls Restladung aus dem Zielkreis. Wenn man das umkehrt, fängt dein eigener Schild die Spitze ab."
             Credits = 800
             Materials = [| CommodityId.MedSupplies, 5; CommodityId.PlasmaCores, 3 |]
             Requires = [| ResonanceCoils |]; Ports = hubs; Relic = -1 }
           { Id = CompositeArmour; Branch = 1; Tier = 3; TierLabel = "SPEZIALISIERUNG"
             Name = "Schlackefeld-Verbundpanzer"
             Source = "Schichtbund · Helios-IV"
             Summary = "+30 Hüllenkapazität und +10 % Wendigkeit für alle Schiffe deiner Flotte."
             Log = "SCHICHTLISTE HELIOS, SOHLE 9: Zwei Tonnen Fehlcharge aus Ofen vier. Steht als Ausschuss in den Büchern, liegt hinten bei Tor zwei."
             Credits = 980
             Materials = [| CommodityId.TitaniumScrap, 5; CommodityId.PlasmaCores, 3; CommodityId.MedSupplies, 2 |]
             Requires = [| ShieldSiphon |]; Ports = hubs; Relic = -1 }
           { Id = PhaseReflector; Branch = 1; Tier = 4; TierLabel = "DURCHBRUCH"
             Name = "TG-4-Spoofingfilter"
             Source = "Band von 2034 · nur Relais Theta"
             Summary = "Booster-Zündung löst alle 12 s einen Abwehrimpuls aus, der feindliche Bolzen im 45-m-Umkreis vernichtet."
             Log = "ARCHIVBAND 14.03.2034: Signal als Spoofing verworfen. Lind hat die alte Filtertabelle umgeschrieben, damit anfliegende Ladungen am Bug abreißen."
             Credits = 1450
             Materials = [| CommodityId.CryoOre, 6; CommodityId.PlasmaCores, 4; CommodityId.NavData, 3 |]
             Requires = [| CompositeArmour |]; Ports = [| theta |]; Relic = -1 }
           { Id = AetherOvercharge; Branch = 1; Tier = 5; TierLabel = "APEX"
             Name = "Sleeve-7-Überladungskern"
             Source = "Wiederherstellungsakte Vance · nur Relais Theta"
             Summary = "Waffen-Overdrive startet bereits ab 2× Kombo und hält +50 % länger."
             Log = "AKTE K. VANCE, SLEEVE 7: Überlast-Freigabe 150 % für Nottransfer. Liegt noch im selben Speicherblock wie die alte Schiffssteuerung."
             Credits = 2400
             Materials = [| CommodityId.PlasmaCores, 6; CommodityId.NavData, 6; CommodityId.MedSupplies, 5 |]
             Requires = [| PhaseReflector; CapacitorCooling |]; Ports = [| theta |]; Relic = 5 }

           // --- Zweig 2: Gürtel-Praxis (Fracht & Bergung) ---
           { Id = AiCargoRouting; Branch = 2; Tier = 1; TierLabel = "GRUNDLAGE"
             Name = "Voss’ Analogkurse"
             Source = "Voss · Bergbau-Depot Kestrel"
             Summary = "+8 m/s Reisetempo für alle Schiffe und +5 % Bonus auf positive Verkaufsmargen."
             Log = "Voss: Alte Papierkarten aus den siebziger Jahren. Die neuen AETHER-Bojen fehlen drauf, dafür stimmen die Abkürzungen."
             Credits = 320
             Materials = [| CommodityId.TitaniumScrap, 3; CommodityId.FuelCells, 3 |]
             Requires = [||]; Ports = hubs; Relic = -1 }
           { Id = QuantumHold; Branch = 2; Tier = 2; TierLabel = "PROTOTYP"
             Name = "Nicht spezifizierte Masse"
             Source = "Kipper Taro · Drift-9"
             Summary = "Erhöht den Frachtraum der gesamten Flotte dauerhaft um +16 t."
             Log = "Kipper, Drift-9: Trennwand raus, Verstrebung nach hinten versetzt. Auf dem Papier bleibt es ein Standardrumpf, rein passen sechzehn Tonnen mehr."
             Credits = 700
             Materials = [| CommodityId.TitaniumScrap, 6; CommodityId.FuelCells, 5 |]
             Requires = [| AiCargoRouting |]; Ports = hubs; Relic = -1 }
           { Id = DeepSensors; Branch = 2; Tier = 3; TierLabel = "SPEZIALISIERUNG"
             Name = "Seras Horchantenne"
             Source = "Sera · Relais Theta"
             Summary = "+45 m Bergungs-Magnetreichweite und +150 m G-Scan-Radius auf Erz & Archive."
             Log = "Sera: Eigentlich hab ich die Spule gebaut, um schwache Notrufe aus dem Nebel zu kriegen. Erzspuren im Fels schlagen darauf genauso an."
             Credits = 920
             Materials = [| CommodityId.NavData, 4; CommodityId.CryoOre, 4; CommodityId.FuelCells, 3 |]
             Requires = [| QuantumHold |]; Ports = hubs; Relic = -1 }
           { Id = TractorField; Branch = 2; Tier = 4; TierLabel = "DURCHBRUCH"
             Name = "Letzte-Leine-Schleppfeld"
             Source = "Odo Karst · Schlepper 44"
             Summary = "+1 zusätzliche Wrackgut-Einheit pro Gegnerabschuss und insgesamt +180 m Sog auf Beute."
             Log = "Odo Karst: Alte Spule von Schlepper 44. Zieht ordentlich Strom beim Anlaufen, holt dafür auch die kleinen Trümmer mit rein."
             Credits = 1300
             Materials = [| CommodityId.CryoOre, 6; CommodityId.NavData, 4; CommodityId.FuelCells, 4 |]
             Requires = [| DeepSensors |]; Ports = hubs; Relic = -1 }
           { Id = TradeMatrix; Branch = 2; Tier = 5; TierLabel = "APEX"
             Name = "Spiegelkennung Graue Wacht"
             Source = "Kipper Taro · nur Drift-9"
             Summary = "Halbiert Geleitschutzgebühren dauerhaft (−50 %) und gewährt +20 % Extramarge bei Verkäufen."
             Log = "Kipper: Ich hab dir dieselbe Kennung geflasht, mit der hier oben gerade Geleitschutz abgerechnet wird. Frag nicht, woher ich die Datei hab."
             Credits = 2100
             Materials = [| CommodityId.NavData, 6; CommodityId.MedSupplies, 5; CommodityId.TitaniumScrap, 6 |]
             Requires = [| TractorField; ShieldSiphon |]; Ports = [| drift |]; Relic = 8 } |]

    let maxMask = (1 <<< nodes.Length) - 1

    let create () : ResearchState =
        { LabInstalled = false; Unlocked = 0 }

    let reset (state: ResearchState) =
        state.LabInstalled <- false
        state.Unlocked <- 0

    let has (state: ResearchState) (id: int) =
        id >= 0 && id < nodes.Length && (state.Unlocked &&& (1 <<< id)) <> 0

    let unlockedCount (state: ResearchState) =
        let mutable n = 0
        for i in 0 .. nodes.Length - 1 do
            if has state i then n <- n + 1
        n

    [<Emit("Number.isInteger($0)")>]
    let private integer (_v: int) : bool = jsNative
    [<Emit("typeof $0 === 'boolean'")>]
    let private boolean (_v: bool) : bool = jsNative

    let valid (state: ResearchState) =
        not (isNull (box state)) &&
        boolean state.LabInstalled &&
        integer state.Unlocked &&
        state.Unlocked >= 0 && state.Unlocked <= maxMask &&
        (state.LabInstalled || state.Unlocked = 0) &&
        (nodes |> Array.forall (fun n ->
            not (has state n.Id) || (n.Requires |> Array.forall (has state))))

    let private hasMaterials (cargo: CargoHold) (materials: (CommodityId * int)[]) =
        materials |> Array.forall (fun (good, qty) -> TradeSystem.quantityOf cargo good >= qty)

    let private consumeMaterials (cargo: CargoHold) (materials: (CommodityId * int)[]) =
        for good, qty in materials do
            let i = int good
            cargo.Quantities.[i] <- cargo.Quantities.[i] - qty
            if cargo.Quantities.[i] = 0 then cargo.AvgCost.[i] <- 0.0

    let canInstallLab (state: ResearchState) (sectorId: string) (wallet: Wallet) (cargo: CargoHold) =
        not state.LabInstalled &&
        isLabPort sectorId &&
        wallet.Credits >= labCredits &&
        hasMaterials cargo labMaterials

    let installLab (state: ResearchState) (sectorId: string) (wallet: Wallet) (cargo: CargoHold) =
        if not (canInstallLab state sectorId wallet cargo) then
            if state.LabInstalled then Error "Bord-Labor bereits installiert."
            elif not (isLabPort sectorId) then Error "Nur an Forschungs-Relais Theta verfügbar."
            elif wallet.Credits < labCredits then Error (sprintf "%d CR fehlen." (labCredits - wallet.Credits))
            else Error "Benötigte Forschungsmaterialien fehlen im Frachtraum."
        else
            wallet.Credits <- wallet.Credits - labCredits
            consumeMaterials cargo labMaterials
            state.LabInstalled <- true
            Ok "RT-9 Bord-Labor eingebaut. Der Bandleser läuft lokal ohne AETHER-Gateway."

    let prerequisitesMet (state: ResearchState) (id: int) =
        id >= 0 && id < nodes.Length && (nodes.[id].Requires |> Array.forall (has state))

    /// Vorstufen, die noch fehlen (für Anzeige und Fehlermeldung).
    let missingRequirements (state: ResearchState) (id: int) =
        nodes.[id].Requires
        |> Array.filter (fun r -> not (has state r))
        |> Array.map (fun r -> nodes.[r].Name)

    let atPort (sectorId: string) (id: int) =
        id >= 0 && id < nodes.Length && Array.contains sectorId nodes.[id].Ports

    /// Ein Apex-Knoten bleibt versiegelt, bis der passende Fund im Archiv liegt.
    let sealBroken (relics: int) (id: int) =
        id >= 0 && id < nodes.Length &&
        (nodes.[id].Relic < 0 || (relics &&& (1 <<< nodes.[id].Relic)) <> 0)

    let canResearch (state: ResearchState) (sectorId: string) (relics: int) (wallet: Wallet) (cargo: CargoHold) (id: int) =
        state.LabInstalled &&
        atPort sectorId id &&
        not (has state id) &&
        prerequisitesMet state id &&
        sealBroken relics id &&
        wallet.Credits >= nodes.[id].Credits &&
        hasMaterials cargo nodes.[id].Materials

    let unlock (state: ResearchState) (sectorId: string) (relics: int) (wallet: Wallet) (cargo: CargoHold) (fleet: HangarFleet) (id: int) =
        if id < 0 || id >= nodes.Length then Error "Unbekannte Technologie."
        elif not state.LabInstalled then Error "Zuerst das RT-9 Bord-Labor auf Relais Theta einbauen."
        elif has state id then Error "Bereits erforscht."
        elif not (prerequisitesMet state id) then
            Error (sprintf "Vorstufe fehlt: %s." (missingRequirements state id |> String.concat ", "))
        elif not (sealBroken relics id) then
            Error (sprintf "Versiegelt. Das Labor braucht den Fund %s." (relicName nodes.[id].Relic))
        elif not (atPort sectorId id) then Error (sprintf "Einbau nur an: %s." (portList nodes.[id].Ports))
        elif wallet.Credits < nodes.[id].Credits then Error (sprintf "%d CR fehlen." (nodes.[id].Credits - wallet.Credits))
        elif not (hasMaterials cargo nodes.[id].Materials) then Error "Benötigte Ressourcen fehlen im Frachtraum."
        else
            let node = nodes.[id]
            wallet.Credits <- wallet.Credits - node.Credits
            consumeMaterials cargo node.Materials
            state.Unlocked <- state.Unlocked ||| (1 <<< id)
            if id = QuantumHold then
                cargo.Capacity <- cargo.Capacity + 16
            if id = CryoDisruptor then
                fleet.Kits <- fleet.Kits ||| (1 <<< ShipCatalog.CryoDisruptor)
            if id = PhaseRailgun then
                fleet.Kits <- fleet.Kits ||| (1 <<< ShipCatalog.PhaseRailgun)
            Ok (sprintf "%s eingebaut. Bandfragment %d/%d lesbar." node.Name (unlockedCount state) nodes.Length)

    // --- Stat-Helfer für Flug, Kampf & Handel ---
    let laserMultiplier state = if has state HighEnergyOptics then 1.08 else 1.0
    let fireIntervalMultiplier state = if has state CapacitorCooling then 0.88 else 1.0
    let missileReloadMultiplier state = if has state CapacitorCooling then 0.85 else 1.0
    let bonusShield state = if has state ResonanceCoils then 20.0 else 0.0
    let bonusHull state = if has state CompositeArmour then 30.0 else 0.0
    let handlingMultiplier state = if has state CompositeArmour then 1.10 else 1.0
    let bonusCruise state = if has state AiCargoRouting then 8.0 else 0.0
    let bonusHold state = if has state QuantumHold then 16 else 0
    let bonusMagnet state =
        (if has state DeepSensors then 45.0 else 0.0) +
        (if has state TractorField then 135.0 else 0.0)
    let bonusScanRange state = if has state DeepSensors then 150.0 else 0.0
    let marginBonusPercent state =
        (if has state AiCargoRouting then 5 else 0) +
        (if has state TradeMatrix then 20 else 0)
    let escortFeeScale state = if has state TradeMatrix then 0.5 else 1.0
