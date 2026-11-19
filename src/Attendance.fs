namespace SpaceEngine

/// One public-duty cruiser, one itinerary on the saved simulation clock.
[<RequireQualifiedAccess>]
module Attendance =
    type Log = { Seed: int; Heard: int }
    type Watch = { Sector: int; Next: int; Slot: int; Elapsed: float }
    type Actor = { Position: Vec3; Rotation: Quat }
    type Subject = Gate | Liner | Pilot | Dock
    type Cue = { Id: int; Callsign: string; Text: string; Clip: string; Reply: bool }

    let sectors =
        Universe.sectors |> Array.indexed |> Array.choose (fun (i,s) ->
            if s.Id = "schleierpass" then None else Some i)
    let duration = 104.0
    let create seed = { Seed=seed; Heard=0 }

    // An odd stride visits every one of the eight ports. Alternating parity also
    // prevents the last stop of a round being the first stop of the next one.
    let private destination seed slot =
        let round = slot / sectors.Length
        let mutable h = uint32 seed + uint32 round * 0x9e3779b9u
        h <- (h ^^^ (h >>> 16)) * 0x85ebca6bu
        h <- h ^^^ (h >>> 13)
        let offset = 2 * int (h &&& 3u) + seed % 2
        let stride = 1 + 2 * int ((h >>> 5) &&& 3u)
        sectors.[(offset + (slot % sectors.Length)*stride) % sectors.Length]

    let watch seed worldTime =
        let time = worldTime + float (seed % 61)
        let slot = int (floor (time/duration))
        { Sector=destination seed slot; Next=destination seed (slot+1)
          Slot=slot; Elapsed=time-float slot*duration }

    // Entry and exit lie beyond the traffic renderer's range from the port.
    // The cruiser slows over the berths and accelerates out over the high lane.
    let actor sector elapsed =
        let u = 2.0 * elapsed / duration - 1.0
        let port = Universe.sectors.[sector].Outpost.Position
        let side = if sector % 2 = 0 then 1.0 else -1.0
        let position = port + Vec3(side*(420.*u+1580.*u*u*u),428.+182.*u*u,175.-1100.*u*u)
        let direction = Vec3(side*(420.+4740.*u*u),364.*u,-2200.*u)
        { Position=position; Rotation=Quat.lookRotation direction Vec3.up }

    let inRange (cruiser: Actor) listener = Vec3.distanceSq cruiser.Position listener <= 900.*900.

    let private paths = Universe.sectors |> Array.mapi (fun sector _ -> lazy (
        if Array.contains sector sectors then Array.init 209 (fun i -> (actor sector (float i*0.5)).Position)
        else Array.empty))

    let inLane sector (p: Vec3) rockRadius =
        let port = Universe.sectors.[sector].Outpost.Position
        let clearance = AttendanceMeshes.radius + rockRadius + 28.
        p.Y >= port.Y+428.-clearance && p.Y <= port.Y+610.+clearance &&
        paths.[sector].Value |> Array.exists (fun center -> Vec3.distanceSq center p < clearance*clearance)

    let subject elapsed (cruiser: Actor) (player: Vec3) =
        if elapsed < 24. then Gate
        elif elapsed < 57. then Liner
        elif elapsed < 85. && player.Y < cruiser.Position.Y-20. && inRange cruiser player then Pilot
        else Dock

    let desk = "Doppel-A / Listenstelle"
    let private hours = [|26.;37.;47.;63.;75.|]
    let private shiftA =
        [| "daa_sicht","Linienverkehr, Kamera bitte kurz an. Mir fehlt noch die Sichtbestätigung."
           "daa_zwei","Wir sind zu zweit an dem Gerät. Uns hört man doch."
           "daa_blatt","Ja, beide. Moment. Ich nehme ein zweites Blatt." |]
    let private shiftB =
        [| "daa_standbild","Linienverkehr, Ihr Bild steht noch auf gestern. Sind Sie heute wieder da?"
           "daa_gestern","Das Bild ist von gestern. Ich bin von heute."
           "daa_heute","Verstanden. Ich trage die Uhrzeit mit der Hand nach." |]

    /// A short reception window tolerates a busy channel. Saving the last cue
    /// prevents reopening the game or returning from the map replaying a line.
    let cue (log: Log) (w: Watch) sector listener =
        if sector <> w.Sector then None
        else
            let mutable index = -1
            for i in 0..hours.Length-1 do
                if w.Elapsed >= hours.[i] && w.Elapsed < hours.[i]+6.0 then index <- i
            let id = 1 + w.Slot*hours.Length + index
            if index < 0 || id <= log.Heard then None
            else
                let ship = actor sector w.Elapsed
                if not (inRange ship listener) || (index >= 3 && subject w.Elapsed ship listener <> Pilot) then None
                else
                    let clip,text =
                        if index < 3 then (if (w.Slot / sectors.Length + sector / 2 + log.Seed / 17) % 2 = 0 then shiftA else shiftB).[index]
                        elif index = 3 then "daa_kurier","Kurierschiff, einmal kurz ins Bild, dann sind wir durch."
                        else "daa_frei","Rückkanal ist da. Das Feld für abwesend lasse ich frei."
                    Some { Id=id; Callsign=(if index = 1 then "Linienverkehr / Rückkanal" else desk)
                           Text=text; Clip=clip; Reply=index=1 }

    let caption = function
        | Gate -> "Torverkehr · Kennungen zählen"
        | Liner -> "Linienverkehr · Bildabgleich"
        | Pilot -> "Kurierkanal · Sichtfenster offen"
        | Dock -> "Liegeplätze · Schichtliste abgleichen"
