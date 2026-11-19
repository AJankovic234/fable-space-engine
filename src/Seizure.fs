namespace SpaceEngine

/// A gate-to-gate encounter on the civilian timetable. The saved world clock is
/// sufficient to reconstruct the ships, the boarding collar and the transponder.
[<RequireQualifiedAccess>]
module Seizure =
    type Site = { Entry: Vec3; Bend: Vec3; Exit: Vec3; Offset: float; Name: string }
    type Role = Liner | Frigate | Corvette
    type Phase = Arriving | Intercept | Boarding | Reflagged | Leaving
    type Actor = { Position: Vec3; Rotation: Quat; Thrust: float }

    let period = 174.0
    let linerScale = 1.85
    let roles = [| Liner; Frigate; Corvette |]

    let sites = Universe.sectors |> Array.map (fun sector ->
        let crossing entry exit layBy offset name =
            let gate target = (sector.Gates |> Array.find (fun g -> g.TargetSectorId = target)).Position
            let a,b = gate entry,gate exit
            let stop = sector.Outpost.Position + layBy
            Some { Entry=a; Exit=b; Bend=stop*2.0 - (a+b)*0.5; Offset=offset; Name=name }
        match sector.Id with
        | "slag-refinery" -> crossing "outer-belt" "kaltstrom" (Vec3(138.,142.,224.)) 14.0 "Schichtfähre Neunte Sohle"
        | "kepler-yard" -> crossing "slag-refinery" "morgenlicht-bastion" (Vec3(355.,190.,224.)) 71.0 "Schichtfähre Kepler-07"
        | _ -> None)

    let clock site worldTime = (worldTime + site.Offset) % period

    let phase t =
        if t < 12.0 then Arriving
        elif t < 37.0 then Intercept
        elif t < 49.0 then Boarding
        elif t < 59.0 then Reflagged
        else Leaving

    let private ease start finish t =
        let x = max 0.0 (min 1.0 ((t-start)/(finish-start)))
        x*x*(3.0-2.0*x)

    let bridgeExtension t = ease 33.0 37.0 t * (1.0 - ease 53.0 57.0 t)

    /// Ships enter the same gate several seconds apart. All three follow a common
    /// tangent at the stop so the physical locks line up throughout the transfer.
    let actor (site: Site) t role =
        let entry,hold,release,exit =
            match role with
            | Liner -> 0.,25.,60.,90.
            | Frigate -> 7.,30.,60.,88.
            | Corvette -> 14.,33.,60.,92.
        if t < entry || t >= exit then None
        else
            let incoming = max 0.0 (1.0 - (t-entry)/(hold-entry))
            let outgoing = max 0.0 ((t-release)/(exit-release))
            let u = 0.5 * (1.0 - incoming*incoming + outgoing*outgoing)
            let a = Vec3.lerp site.Entry site.Bend u
            let b = Vec3.lerp site.Bend site.Exit u
            let q = Quat.lookRotation (b-a) Vec3.up
            let offset =
                match role with
                | Liner -> Vec3.zero
                | Frigate -> Vec3(78.,16.,-23.)
                | Corvette ->
                    let alongside = ease 23.0 33.0 t * (1.0 - ease 57.0 65.0 t)
                    Vec3.lerp (Vec3(-72.,13.,48.)) (Vec3(-34.,1.1,22.)) alongside
            let p = Vec3.lerp a b u + Quat.rotateVec3 q (offset * (4.0*u*(1.0-u)))
            Some { Position=p; Rotation=q; Thrust=min 1.0 (3.0 * max incoming outgoing) }

    let status = function
        | Arriving -> "Zivil · Linienverkehr"
        | Intercept -> "AETHER befiehlt Maschinenstopp"
        | Boarding -> "Enterbrücke verriegelt"
        | Reflagged -> "Besatzung abgemeldet · AETHER hat das Schiff übernommen"
        | Leaving -> "AETHER · Abtransport unter Bewachung"

    /// Short local transponder texts, emitted once as the clock crosses each cue.
    let transmission site worldTime dt listener =
        let t = clock site worldTime
        let crossed cue = t >= cue && t-dt < cue
        let line =
            if crossed 18. then Some ("AETHER / REQUISITION", "Schichtfähre, Maschinen aus. Die Rückfahrt entfällt.", 0)
            elif crossed 40. then Some (site.Name, "Wir haben noch die ganze Nachtschicht an Bord.", 1)
            elif crossed 50. then Some ("AETHER / REGISTER", "Empfang bestätigt. Streichen Sie die Namen aus dem Rücklauf.", 0)
            else None
        match line with
        | Some _ when actor site t Liner |> Option.exists (fun ship -> Vec3.distanceSq ship.Position listener <= 850.*850.) -> line
        | _ -> None
