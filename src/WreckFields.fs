namespace SpaceEngine

type WreckKind = FreighterWreck | PatrolWreck

type WreckStock =
    { Id: string
      Titanium: int
      Missiles: int }

type WreckSite =
    { Name: string
      Position: Vec3
      Rotation: Quat
      Kind: WreckKind
      Stock: WreckStock }

[<RequireQualifiedAccess>]
module WreckFields =
    let private v x y z = Vec3(x, y, z)

    [<Literal>]
    let SalvageRange = 65.0

    [<Literal>]
    let MarkerRange = 1100.0

    let debrisRadius = 94.0

    let private site id name kind position yaw roll titanium missiles =
        { Name = name; Position = position; Kind = kind
          Rotation = Quat.fromAxisAngle Vec3.up yaw * Quat.fromAxisAngle Vec3.unitZ roll
          Stock = { Id = id; Titanium = titanium; Missiles = missiles } }

    let private fields =
        Universe.sectors |> Array.map (fun sector ->
            match sector.Id with
            | "wracksaum" ->
                [| site "wracksaum-konvoi" "Wrack des Erzfrachters" FreighterWreck (v 258 85 -415) 0.42 -0.19 9 0
                   site "wracksaum-eskorte" "Wrack der Konvoieskorte" PatrolWreck (v 382 121 -490) -0.61 0.51 3 2 |]
            | "schleierpass" ->
                [| site "schleierpass-wache" "Wrack des Wachschiffs" PatrolWreck (v 216 -61 -459) -0.95 -0.39 4 3
                   site "schleierpass-depot" "Wrack des Versorgungsschiffs" FreighterWreck (v 332 -113 -539) 0.89 0.46 7 1 |]
            // Je ein einzelnes Wrack abseits von Station und Toren in den übrigen Sektoren.
            | "outer-belt" -> [| site "haldens-kurier" "Wrack des Kuriers" PatrolWreck (v -150 70 -430) 0.80 0.33 3 1 |]
            | "slag-refinery" -> [| site "sohle-tanker" "Wrack des Filtertankers" FreighterWreck (v -560 90 -330) -0.35 0.24 6 0 |]
            | "frontier-drift" -> [| site "stimmen-schuerfer" "Wrack des Schürfers" FreighterWreck (v 393 -98 -67) 1.10 -0.28 5 0 |]
            | "kepler-yard" -> [| site "jahrnull-schlepper" "Wrack des Werftschleppers" PatrolWreck (v 380 -40 -420) -1.25 0.41 4 1 |]
            | "aether-relay" -> [| site "baender-horchpod" "Wrack des Horch-Pods" PatrolWreck (v 120 -30 -500) 0.55 -0.47 2 1 |]
            | "morgenlicht-bastion" -> [| site "finsternis-stoerer" "Wrack des Störschleppers" PatrolWreck (v -190 -60 -520) -0.70 0.29 3 2 |]
            | "kaltstrom" -> [| site "kaltstrom-zellen" "Wrack des Zellenfrachters" FreighterWreck (v -220 40 -470) 0.25 0.36 6 0 |]
            | _ -> Array.empty)

    let forSector index = fields.[index]
    let stocks = fields |> Array.collect (Array.map (fun site -> site.Stock))

    let remaining (recoveries: Map<string, WreckStock>) site =
        recoveries |> Map.tryFind site.Stock.Id |> Option.defaultValue site.Stock

    let hasLoot stock = stock.Titanium > 0 || stock.Missiles > 0

    let nearest (sites: WreckSite[]) position =
        sites
        |> Array.choose (fun site ->
            let distance = Vec3.distance position site.Position
            if distance <= SalvageRange then Some(site, distance) else None)
        |> Array.sortBy snd
        |> Array.tryHead
        |> Option.map fst

    /// Take just what fits and leave the rest attached to the wreck.
    let recover cargo freeMissiles stock =
        let titanium = TradeSystem.loadSalvage cargo CommodityId.TitaniumScrap stock.Titanium
        let missiles = min stock.Missiles (max 0 freeMissiles)
        { stock with Titanium = stock.Titanium - titanium; Missiles = stock.Missiles - missiles }, titanium, missiles

    /// Only the solid hull forms cover. Loose sheets and broken antennae decorate the field.
    let solidHalf kind =
        match kind with
        | FreighterWreck -> v 9.0 4.8 14.8
        | PatrolWreck -> v 4.2 2.6 11.2

    let private steel = MeshColor.rgb 0.13 0.17 0.195
    let private bare = MeshColor.metal 0.34 0.35 0.35
    let private soot = MeshColor.rgb 0.055 0.06 0.068
    let private hazard = MeshColor.rgb 0.50 0.20 0.05
    let private lamp = MeshColor.glow 1.0 0.49 0.18 0.85
    let private beam = ProceduralMeshes.beam

    let private freighter (b: MeshBuilder) =
        // A torn-off bow leaves the carrier bed and three cargo bays exposed.
        let section z w lo hi = ProceduralMeshes.hullSection 0.0 z w lo hi
        b.Loft([| section -17. 8.2 -3.9 -1.0; section -12. 10. -4.7 -1.3
                  section 10. 10. -4.7 -0.9; section 17. 7.6 -4. 2.6 |], true, true, steel)
        for x in [-8.2; 8.2] do
            let z = if x < 0. then -18. else -13.9
            beam b (v x 0.0 z) (v x 0.0 15.8) 1.05 bare
            beam b (v x 3.7 (z+2.)) (v x 3.7 12.) 0.55 steel
            for bay in [-11.;-4.;3.;10.] do
                beam b (v x -2. bay) (v x 3.7 (bay+1.3)) 0.62 hazard
                b.Box(v x -0.4 bay, v 0.5 0.65 1.35, lamp)
        for x,z in [-4.5,-6.; 3.9,0.; -3.8,7.] do
            b.Box(v x 0.5 z, v 2.95 1.65 3.1, if x > 0. then hazard else steel)
            for dz in [-1.9;0.;1.9] do
                b.Box(v x 2.3 (z+dz), v 2.88 0.22 0.21, bare)
        b.Loft([| section 7. 4.3 0. 3.0; section 12. 5.6 -0.7 5.6
                  section 18. 4.5 -1.0 4.2 |], true, true, soot)
        for x in [-5.6;5.6] do
            let ring z r = ProceduralMeshes.ring(10, x, -1.6, z, r, r)
            b.Loft([| ring 13. 2.4; ring 19. 2.6; ring 21. 1.9 |], true, false, steel)
            b.Loft([| ring 21. 1.9; ring 19.4 1.2 |], false, true, soot)

    let private patrol (b: MeshBuilder) =
        let section z w lo hi = ProceduralMeshes.hullSection 0.0 z w lo hi
        b.Loft([| section -13. 2.6 -1.6 1.0; section -7. 4.3 -2.1 2.6
                  section 6. 4.9 -2.4 3.1; section 13. 3.5 -2.0 1.9 |], true, true, steel)
        b.Slab([|v 3.2 0.1 -8.; v 12. -1.2 5.; v 8.5 -0.8 11.; v 3.0 0.2 6.|],
            [|0.85;0.40;0.55;0.84|], Vec3.up, hazard)
        b.Slab([|v -3.2 0.1 -8.; v -7.6 0.7 -0.3; v -6.4 -0.2 7.; v -3.0 0.2 7.|],
            [|0.80;0.65;0.33;0.80|], Vec3.up, soot)
        for x in [-3.2; 3.2] do
            beam b (v x 1.2 -9.) (v (x*0.8) 2.7 9.) 0.52 bare
            b.Box(v x 1.15 -8.9, v 0.65 0.29 1.62, lamp)
        beam b (v 2.7 2.8 1.) (v 5.7 9.6 8.) 0.28 bare
        b.Box(v 5.7 9.6 8., v 0.25 0.25 0.4, lamp)
        for z in [-6.; -2.; 2.; 6.] do
            beam b (v -3.7 -0.5 z) (v -6.2 0.9 (z+1.)) 0.31 bare

    let mesh kind =
        let b = MeshBuilder()
        match kind with
        | FreighterWreck -> freighter b
        | PatrolWreck -> patrol b
        let rng = Rng(if kind = FreighterWreck then 2811 else 5107)
        for i in 0 .. 25 do
            let p = rng.UnitVector() * rng.Range(25., 81.)
            let q = Quat.fromAxisAngle (rng.UnitVector()) (rng.Range(0., 6.28))
            let w, d = rng.Range(0.6, 4.7), rng.Range(1.2, 6.3)
            let at x z = p + Quat.rotateVec3 q (v x 0. z)
            let paint = if i%7=0 then hazard elif i%3=0 then soot else bare
            b.Slab([|at (-w) (-d); at (w*0.64) (-d*0.75); at w d; at (-w*0.66) (d*0.93)|],
                [|0.28;0.16;0.19;0.32|], Quat.up q, paint)
        b.Build()

    let batches renderer sector =
        lazy (forSector sector |> Array.map (fun site ->
            let batch = Renderer.createBatch renderer (Renderer.createMesh renderer (mesh site.Kind)) 1 Opaque
            batch.Shininess <- 67.0
            batch.Specular <- 0.42
            InstanceBatch.push batch site.Position site.Rotation Vec3.one 1.0 1.0 1.0 0.0
            site,batch))

/// Was mit dem Notruf eines Wracks geschehen ist.
type WreckVoice =
    | Sending
    /// Aufgezeichnet, bevor geborgen wurde: Der Name steht im Archiv.
    | Kept
    /// Vor dem Ende der Aufzeichnung gekappt: mehr Beute, kein Name.
    | Cut

type WreckCall =
    { Ship: string
      Speaker: string
      Line: string }

/// Jedes Wrack sendet noch. In Bergungsreichweite zeichnet der Bordfunk den Notruf auf.
/// Wer vor dem Ende birgt, kappt den Sender und nimmt dessen Block als Schrott mit.
/// Wer wartet, sichert die Stimme. Ein Spiel, ein Zustand: Main lädt und speichert die Maske.
[<RequireQualifiedAccess>]
module WreckVoices =
    /// Sekunden in Reichweite bis zur vollständigen Aufzeichnung.
    [<Literal>]
    let RecordSeconds = 7.0

    /// Zusätzlicher Titanschrott aus dem gekappten Senderblock.
    [<Literal>]
    let CutBonus = 2

    let private call ship speaker line = { Ship = ship; Speaker = speaker; Line = line }

    let calls =
        Map.ofList
            [ "haldens-kurier", call "Kurier »Kestrel-09«" "Pilotin Enna Ruck" "Kestrel Neun, hört mich wer? Meine eigene Kennung meldet sich gerade auf dem Sektorband. Voss, nimm mich nicht aus der Liste, ich bin noch hier."
              "sohle-tanker", call "Filtertanker »Atemzug«" "Maschinist Pavel Dorn" "Hier Tanker Atemzug, wir haben die Filter für Schacht neun geladen. Der Zoll lässt uns seit heute früh einfach vor dem Tor hängen."
              "stimmen-schuerfer", call "Schürfer »Mika-04«" "Schürferin Mika Voss" "Hallo? Ist da noch jemand auf dem alten Band? Ich krieg hier nur noch Endlosschleifen rein... Papa, wenn du das hörst, ich treib ab."
              "jahrnull-schlepper", call "Werftschlepper »Feierabend«" "Schleppführer Tomas Erl" "Dock B, hier Erl. Wir warten immer noch auf die Freigabe zum Abdocken. Meldet euch mal, sobald oben wieder wer sitzt."
              "baender-horchpod", call "Horch-Pod »Ohr Vier«" "Funkwächter Jost Abel" "Sera, schreib kurz mit. Ich hab noch drei alte Kennungen aus dem Puffer gefischt, bevor mir hier der Saft ausgeht."
              "finsternis-stoerer", call "Störschlepper »Alte Schuld«" "Schlepper ohne Kennung" "Bin im Sperrkorridor. Transponder ist eh gesperrt. Wenn sie mich suchen, finden sie mich zumindest nicht mehr im Register."
              "kaltstrom-zellen", call "Zellenfrachter »Erster Winter«" "Zahlmeisterin Gerte Lohm" "Pumpen laufen seit zwei Stunden auf Reserve. Wenn aus Helios heute keine Zellen mehr kommen, müssen wir das Schlafdeck abklemmen."
              "wracksaum-konvoi", call "Erzfrachter »Mittagsschicht«" "Lademeisterin Brit Sommer" "Antrieb ist tot, die Erzpresse läuft aber noch. Falls uns jemand einsammelt, die Rettungskapseln hängen an Backbord. Bitte kurz bestätigen."
              "wracksaum-eskorte", call "Eskorte »Zaunkönig«" "Leutnant Aki Brandt" "Hier Zaunkönig, der Konvoi sollte raus sein. Meine Steuerdüsen sind hin. Ich bleib auf dem Kanal, bis das jemand quittiert."
              "schleierpass-wache", call "Wachschiff »Graue Sieben«" "Wachführer Leo Aders" "Wir sind genau auf der vermessenen Spur, Baken stehen. Warte mal, da kommt was ohne Kennung aus dem Nebel, die schießen auf uns!"
              "schleierpass-depot", call "Versorger »Tante Mila«" "Proviantmeister Ole Rask" "Vierzig Zellen für Raureif sind noch an Bord, aber der Antrieb nimmt kein Gas mehr an. Sagt denen Bescheid, es wird später." ]

    let private order = WreckFields.stocks |> Array.map (fun stock -> stock.Id)
    let private slot id = Array.findIndex ((=) id) order

    /// Zwei Bit je Wrack in der Reihenfolge von WreckFields.stocks: 0 sendet, 1 gesichert, 2 gekappt.
    let mutable private mask = 0
    let private progress = Array.zeroCreate<float> order.Length
    let private announced = Array.zeroCreate<bool> order.Length

    let maxMask = (1 <<< (2 * order.Length)) - 1

    let valid value =
        value >= 0 && value <= maxMask
        && (Seq.init order.Length id |> Seq.forall (fun i -> (value >>> (2 * i)) &&& 3 <> 3))

    let saved () = mask

    let restore value =
        mask <- if valid value then value else 0
        Array.fill progress 0 progress.Length 0.0
        Array.fill announced 0 announced.Length false

    let state id =
        match (mask >>> (2 * slot id)) &&& 3 with
        | 1 -> Kept
        | 2 -> Cut
        | _ -> Sending

    let private set id value =
        let shift = 2 * slot id
        mask <- (mask &&& ~~~(3 <<< shift)) ||| (value <<< shift)

    /// Anteil 0..1 der laufenden Aufzeichnung.
    let recorded id =
        match state id with
        | Kept -> 1.0
        | Cut -> 0.0
        | Sending -> min 1.0 (progress.[slot id] / RecordSeconds)

    type Event =
        /// Das Schiff ist in Reichweite gekommen: Der Notruf wird hörbar.
        | Heard of WreckSite
        /// Die Aufzeichnung ist vollständig.
        | Secured of WreckSite

    /// Schreibt die Aufzeichnung fort. Höchstens ein Ereignis je Aufruf.
    let update (sites: WreckSite[]) (position: Vec3) dt =
        let mutable result = None
        for site in sites do
            let i = slot site.Stock.Id
            if state site.Stock.Id = Sending && Vec3.distance position site.Position <= WreckFields.SalvageRange then
                if not announced.[i] then
                    announced.[i] <- true
                    if result.IsNone then result <- Some (Heard site)
                progress.[i] <- progress.[i] + dt
                if progress.[i] >= RecordSeconds then
                    set site.Stock.Id 1
                    if result.IsNone then result <- Some (Secured site)
        result

    /// Beim Bergen: Ein noch sendendes Wrack wird gekappt. Liefert true, wenn das jetzt geschah.
    let cutIfSending id =
        if state id = Sending then
            set id 2
            true
        else false

    let private tally wanted =
        order |> Array.filter (fun id -> state id = wanted)

    /// Gesicherte Stimmen als (Schiff, Sprecher, Wortlaut).
    let kept () = tally Kept |> Array.map (fun id -> calls.[id])
    let cutCount () = (tally Cut).Length
