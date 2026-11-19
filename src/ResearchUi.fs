namespace SpaceEngine

open Browser.Dom
open Browser.Types

type ResearchUiRequest =
    | InstallLab
    | UnlockTech of int

type ResearchUi =
    { LabInstallation: HTMLElement
      LabMaterials: HTMLElement
      LabButton: HTMLButtonElement
      StatusBanner: HTMLElement
      TreeRoot: HTMLElement
      NodeArticles: HTMLElement[]
      NodeGates: HTMLElement[]
      NodeLogs: HTMLElement[]
      NodeMaterials: HTMLElement[]
      NodeButtons: HTMLButtonElement[]
      mutable Request: ResearchUiRequest option }

[<RequireQualifiedAccess>]
module ResearchUi =
    let private materialBadges (cargo: CargoHold) (materials: (CommodityId * int)[]) =
        materials
        |> Array.map (fun (good, need) ->
            let have = TradeSystem.quantityOf cargo good
            let def = TradeSystem.commodityDef good
            let cls = if have >= need then "mat-pill ok" else "mat-pill missing"
            sprintf "<span class='%s'>%s %d/%d</span>" cls def.ShortName have need)
        |> String.concat ""

    let private sealedLog = "▒▒▒ Bandfragment unlesbar · wird mit dem Einbau entschlüsselt ▒▒▒"

    let create () =
        let get id = document.getElementById id
        let treeRoot = get "research-tree"
        let articles = Array.zeroCreate<HTMLElement> Research.nodes.Length
        let gates = Array.zeroCreate<HTMLElement> Research.nodes.Length
        let logs = Array.zeroCreate<HTMLElement> Research.nodes.Length
        let mats = Array.zeroCreate<HTMLElement> Research.nodes.Length
        let buttons = Array.zeroCreate<HTMLButtonElement> Research.nodes.Length

        for branch in 0 .. Research.branchNames.Length - 1 do
            let col = document.createElement "section"
            col.className <- "research-branch"
            col.setAttribute ("data-branch", string branch)
            let heading = document.createElement "h3"
            heading.textContent <- Research.branchNames.[branch]
            col.appendChild heading |> ignore
            let source = document.createElement "p"
            source.className <- "research-branch-source"
            source.textContent <- Research.branchSources.[branch]
            col.appendChild source |> ignore

            for node in Research.nodes do
                if node.Branch = branch then
                    let card = document.createElement "article"
                    card.className <- "research-node"
                    card.setAttribute ("data-node", string node.Id)
                    card.setAttribute ("data-tier", string node.Tier)
                    // The atlas has one row per branch and one column per tier.
                    let artPosition = sprintf "%d%% %d%%" ((node.Tier - 1) * 25) (branch * 50)
                    card.innerHTML <-
                        "<div class='research-node-head'><span aria-hidden='true' class='research-node-art' style='background-position:" + artPosition + "'></span><div>" +
                        "<div class='research-node-top'><span class='tier-tag'>" + node.TierLabel + "</span><strong class='research-cost'>" + string node.Credits + " CR</strong></div>" +
                        "<h4>" + node.Name + "</h4>" +
                        "<p class='research-source'>" + node.Source + "</p></div></div>" +
                        "<p>" + node.Summary + "</p>" +
                        "<p class='research-gate'></p>" +
                        "<blockquote class='research-log'></blockquote>" +
                        "<div class='research-mats'></div>" +
                        "<button type='button' class='research-btn'></button>"
                    col.appendChild card |> ignore
                    articles.[node.Id] <- card
                    gates.[node.Id] <- card.querySelector ".research-gate" :?> HTMLElement
                    logs.[node.Id] <- card.querySelector ".research-log" :?> HTMLElement
                    mats.[node.Id] <- card.querySelector ".research-mats" :?> HTMLElement
                    buttons.[node.Id] <- card.querySelector ".research-btn" :?> HTMLButtonElement

            treeRoot.appendChild col |> ignore

        let ui =
            { LabInstallation = get "research-lab-installation"
              LabMaterials = get "research-lab-mats"
              LabButton = get "research-lab-install" :?> HTMLButtonElement
              StatusBanner = get "research-status"
              TreeRoot = treeRoot
              NodeArticles = articles
              NodeGates = gates
              NodeLogs = logs
              NodeMaterials = mats
              NodeButtons = buttons
              Request = None }

        ui.LabButton.addEventListener ("click", fun _ -> ui.Request <- Some InstallLab)
        for i in 0 .. buttons.Length - 1 do
            buttons.[i].addEventListener ("click", fun _ -> ui.Request <- Some (UnlockTech i))
        ui

    /// Offene Bedingungen eines Knotens: fehlende Vorstufen (auch aus Nachbarzweigen) und das Siegel.
    let private gateText (state: ResearchState) (relics: int) (node: ResearchNode) =
        let missing = Research.missingRequirements state node.Id
        let parts =
            [| if missing.Length > 0 then
                   yield "Braucht: " + String.concat " + " missing
               if not (Research.sealBroken relics node.Id) then
                   yield "Versiegelt bis zum Fund: " + Research.relicName node.Relic
               elif node.Relic >= 0 then
                   yield "Siegel gebrochen: " + Research.relicName node.Relic |]
        String.concat " · " parts

    let refresh (ui: ResearchUi) (state: ResearchState) (sectorId: string) (relics: int) (wallet: Wallet) (cargo: CargoHold) (trading: bool) =
        let tab = document.getElementById "tab-research" :?> HTMLButtonElement
        let badge = document.getElementById "research-balance"
        if not trading then
            tab.setAttribute ("hidden", "")
            if tab.getAttribute "aria-pressed" = "true" then (document.getElementById "tab-market" :?> HTMLButtonElement).click ()
        else
            tab.removeAttribute "hidden"
            let total = Research.nodes.Length
            let doneCount = Research.unlockedCount state
            badge.textContent <-
                if not state.LabInstalled then "RT-9"
                else sprintf "%d/%d" doneCount total

            let atLabPort = Research.isLabPort sectorId

            if not state.LabInstalled then
                ui.LabInstallation.removeAttribute "hidden"
                ui.LabMaterials.innerHTML <- materialBadges cargo Research.labMaterials
                let canInstall = Research.canInstallLab state sectorId wallet cargo
                ui.LabButton.disabled <- not canInstall
                ui.LabButton.textContent <-
                    if canInstall then sprintf "RT-9 BORD-LABOR EINBAUEN · %d CR" Research.labCredits
                    elif not atLabPort then sprintf "NUR AN FORSCHUNGS-RELAIS THETA · %d CR" Research.labCredits
                    elif wallet.Credits < Research.labCredits then sprintf "%d CR FEHLEN" (Research.labCredits - wallet.Credits)
                    else "MATERIALIEN IM FRACHTRAUM FEHLEN"
                ui.StatusBanner.textContent <-
                    if atLabPort then "Relais Theta erreicht · Dr. Mara Lind übernimmt den Einbau."
                    else "Einbauort: Forschungs-Relais Theta · Der Bandleser fehlt an Bord."
            else
                ui.LabInstallation.setAttribute ("hidden", "")
                let here =
                    Research.nodes
                    |> Array.filter (fun n ->
                        not (Research.has state n.Id) && Research.atPort sectorId n.Id &&
                        Research.prerequisitesMet state n.Id && Research.sealBroken relics n.Id)
                    |> Array.length
                ui.StatusBanner.textContent <-
                    if doneCount = total then
                        "RT-9 · 15/15 Fragmente lesbar. Das Band ist vollständig. AETHER führt keins dieser Bauteile im Register."
                    elif here > 0 then
                        sprintf "RT-9 · %d/%d Fragmente lesbar · An diesem Dock sind %d Einbauten möglich." doneCount total here
                    else
                        sprintf "RT-9 · %d/%d Fragmente lesbar · Hier ist nichts einbaubar. Jeder Knoten nennt sein Dock, seine Vorstufen und sein Siegel." doneCount total

            for node in Research.nodes do
                let i = node.Id
                let unlocked = Research.has state i
                let prereqs = Research.prerequisitesMet state i
                let sealOpen = Research.sealBroken relics i
                let atPort = Research.atPort sectorId i
                let ready = Research.canResearch state sectorId relics wallet cargo i
                ui.NodeMaterials.[i].innerHTML <-
                    if unlocked then "<span class='mat-pill done'>EINGEBAUT</span>"
                    else materialBadges cargo node.Materials
                ui.NodeGates.[i].textContent <- if unlocked then "" else gateText state relics node
                ui.NodeLogs.[i].textContent <- if unlocked then node.Log else sealedLog
                let nodeState, label =
                    if unlocked then "unlocked", "FRAGMENT GELESEN · AKTIV"
                    elif not state.LabInstalled then "locked", "Bord-Labor nötig"
                    elif not prereqs then "locked", "Vorstufe nötig"
                    elif not sealOpen then "sealed", "Versiegelt · Fund fehlt"
                    elif not atPort then "hub", "Nur " + Research.portList node.Ports
                    elif wallet.Credits < node.Credits then "locked", sprintf "%d CR fehlen" (node.Credits - wallet.Credits)
                    elif not ready then "locked", "Ressourcen fehlen"
                    else "ready", sprintf "EINBAUEN · %d CR" node.Credits
                ui.NodeArticles.[i].setAttribute ("data-state", nodeState)
                ui.NodeButtons.[i].disabled <- not ready
                ui.NodeButtons.[i].textContent <- label
