namespace SpaceEngine

/// PaidTargets survives a second practice flight. Each live round earns its bounty once.
type FlightSchoolState = { mutable Step: int; mutable PaidTargets: int }

module FlightSchool =
    [<Literal>]
    let Off = 0
    [<Literal>]
    let Accelerate = 1
    [<Literal>]
    let Brake = 2
    [<Literal>]
    let FirstTarget = 3
    [<Literal>]
    let MovingTarget = 4
    [<Literal>]
    let Dock = 5

    let create () = { Step = Off; PaidTargets = 0 }
    let onRange step = step = FirstTarget || step = MovingTarget

    let helmStep step throttle braking speed =
        if step = Accelerate && throttle > 0.0 && speed >= 18.0 then Brake
        elif step = Brake && braking && speed < 4.0 then FirstTarget
        else step

    let targetDown state =
        if not (onRange state.Step) then 0
        else
            let number = if state.Step = FirstTarget then 1 else 2
            let bounty = if number > state.PaidTargets then 100 else 0
            state.PaidTargets <- max state.PaidTargets number
            state.Step <- if number = 1 then MovingTarget else Dock
            bounty

    /// The second target moves sideways. Neither target runs hostile AI.
    let guideTarget step time (target: Ship) =
        target.Controls.Throttle <- 0.0
        target.Controls.Fire <- false
        target.Controls.Boost <- false
        target.Velocity <-
            if step = MovingTarget then Quat.right target.Rotation * (3.8 * cos (time * 0.72))
            else Vec3.zero
