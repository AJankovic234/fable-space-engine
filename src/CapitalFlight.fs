namespace SpaceEngine

/// The cruiser, its escort and a torpedo in flight belong to this trade campaign.
type CapitalFlightState =
    { mutable Role: int; mutable Hits: int; mutable Defeated: int
      Guns: float[]; mutable Lock: float; mutable Ammo: int
      mutable Flight: float; mutable Launch: Vec3; mutable Approach: float
      mutable WingHull: float; mutable Volley: float; mutable NextGun: int
      mutable ParkedHull: float; mutable ParkedShield: float; mutable Landing: float }

module CapitalFlight =
    [<Literal>]
    let Bomber = 1
    [<Literal>]
    let Escort = 2
    let lockTime = 2.4
    let range = 420.0
    let landingTime = 16.0
    let freightSix (port: Vec3) berth elapsed =
        let t = elapsed / landingTime
        Vec3.lerp (port + Vec3(-71.0,21.0,174.0)) berth (t*t*(3.0-2.0*t))
    let position = Universe.sectors.[1].Gates.[1].Position + Vec3(-42.0,-12.0,72.0)
    /// The cruiser is modelled in design units (CapitalMeshes) and flown at this scale:
    /// roughly 400 m from drive bells to ram, about 80 times the length of a cutter.
    let hullScale = 2.2
    let gunOffsets = [|Vec3(-35.0,8.5,16.0); Vec3(33.0,8.5,16.0)|] |> Array.map (fun v -> v * hullScale)
    let gunRadius = 6.5 * hullScale
    /// Bolts leave the battery ahead of its barrels instead of inside the turret house.
    let muzzleReach = 9.0 * hullScale
    // Collision volumes follow the hull from the drive block to the ram, then the
    // superstructure, keel and the flank blades.
    let plates =
        [| Vec3(-68.0,0.0,0.0),Vec3(13.0,10.0,12.5)
           Vec3(-62.0,-1.0,0.0),Vec3(19.0,5.0,23.5)
           Vec3(-36.0,-0.5,0.0),Vec3(25.0,11.0,19.0)
           Vec3(0.0,0.0,0.0),Vec3(11.0,10.5,16.5)
           Vec3(26.0,-0.5,0.0),Vec3(15.0,11.0,18.0)
           Vec3(58.0,-2.0,0.0),Vec3(17.0,8.5,12.5)
           Vec3(84.0,-3.0,0.0),Vec3(13.0,5.0,5.0)
           Vec3(-20.0,17.5,0.0),Vec3(34.0,7.5,12.5)
           Vec3(-19.0,30.0,0.0),Vec3(11.0,6.5,12.0)
           Vec3(-21.0,-17.5,0.0),Vec3(29.5,7.5,1.5)
           Vec3(12.0,-2.0,0.0),Vec3(18.0,1.5,19.5)
           Vec3(62.0,-10.0,0.0),Vec3(18.0,5.0,2.2) |]
        |> Array.map (fun (center, half) -> center * hullScale, half * hullScale)
    /// Largest distance of any armour corner from the hull origin (broad-phase sphere).
    let reach = plates |> Array.map (fun (center, half) -> Vec3.length center + Vec3.length half) |> Array.max
    /// Axis-aligned half extents of all armour, for clear lanes around the hull.
    let extent =
        plates |> Array.fold (fun (e: Vec3) (c, h) ->
            Vec3(max e.X (abs c.X + h.X), max e.Y (abs c.Y + h.Y), max e.Z (abs c.Z + h.Z))) Vec3.zero
    /// Height of the mast head above the hull origin; HUD labels sit above it.
    let crown = 50.0 * hullScale

    let create () =
        { Role=0; Hits=0; Defeated=0; Guns=[|32.0;32.0|]; Lock=0.0; Ammo=0
          Flight=0.0; Launch=Vec3.zero; Approach=0.0; WingHull=100.0; Volley=2.5; NextGun=0
          ParkedHull=0.0; ParkedShield=0.0; Landing=0.0 }

    let beginSortie state role hull shield =
        state.Role <- role
        state.Lock <- 0.0
        state.Flight <- 0.0
        state.Ammo <- 2
        state.Approach <- 0.0
        state.WingHull <- 100.0
        state.Volley <- 3.5
        state.ParkedHull <- hull
        state.ParkedShield <- shield

    let standDown state =
        state.Role <- 0
        state.Lock <- 0.0
        state.Flight <- 0.0
        state.Ammo <- 0
        state.ParkedHull <- 0.0
        state.ParkedShield <- 0.0

    let allyPosition state =
        position + Vec3(-26.0,18.0,345.0 - 160.0 * state.Approach)

    let inCone point heading =
        let offset = position - point
        let distance = Vec3.length offset
        distance <= range && distance >= 35.0 && Vec3.dot heading offset >= distance * 0.956

    let torpedoPosition state = Vec3.lerp position state.Launch state.Flight

    /// The projectile strikes only after its travel time. The two roles use the same rack.
    let advance state point heading screenClear fire dt =
        if state.Role = Escort then state.Approach <- min 1.0 (state.Approach + dt / 15.0)
        if state.Flight > 0.0 then
            state.Flight <- max 0.0 (state.Flight - dt * 145.0 / max 1.0 (Vec3.distance position state.Launch))
            if state.Flight = 0.0 then state.Hits <- min 2 (state.Hits + 1)
        elif state.Role <> 0 && state.Ammo > 0 && state.Hits < 2 then
            let tracking =
                if state.Role = Escort then screenClear && state.Approach >= 0.5
                else inCone point heading
            state.Lock <- if tracking then min lockTime (state.Lock + dt) else max 0.0 (state.Lock - dt * 2.0)
            if tracking && state.Lock >= lockTime && (state.Role = Escort || fire) then
                state.Launch <- if state.Role = Escort then allyPosition state else point
                state.Flight <- 1.0
                state.Lock <- 0.0
                state.Ammo <- state.Ammo - 1
