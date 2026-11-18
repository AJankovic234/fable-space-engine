namespace SpaceEngine

type OutfitVisuals =
    { Modules: Lazy<InstanceBatch>[][]
      Relay: Lazy<InstanceBatch>
      Visible: ResizeArray<InstanceBatch> }

[<RequireQualifiedAccess>]
module OutfitVisuals =
    let create renderer =
        let batch geometry = lazy (
            let item = Renderer.createBatch renderer (Renderer.createMesh renderer (geometry ())) 2 Opaque
            item.Shininess <- 72.0
            item.Specular <- 0.55
            item)
        let tiers build = Array.init 3 (fun i -> batch (fun () -> build (i + 1)))
        { Modules = [| tiers UpgradeMeshes.laser; tiers UpgradeMeshes.shield; tiers UpgradeMeshes.armour |]
          Relay = batch UpgradeMeshes.relay
          Visible = ResizeArray<InstanceBatch>(4) }

    let private pair x y z = [| Vec3(-x, y, z); Vec3(x, y, z) |]

    let private guns =
        [| ShipHardpoints.playerGunLeft; ShipHardpoints.playerGunRight |]
        |> Array.map (fun gun -> gun + Vec3(0.0, -0.18, -0.19))

    // Each row follows the skin of one hull. Hold the central turret flange and freight bays clear.
    let private mounts =
        [| [| guns; pair 0.62 0.30 1.45; pair 0.66 0.422 0.53; [| Vec3(0.0, 0.25, 1.45) |] |]
           [| guns; pair 1.62 0.21 0.91; pair 1.62 0.215 -1.40; [| Vec3(0.0, 0.29, 1.42) |] |]
           [| guns; pair 1.57 0.037 1.08; pair 0.65 0.202 -0.12; [| Vec3(0.0, 0.29, 1.46) |] |] |]

    let clear outfit =
        for batch in outfit.Visible do InstanceBatch.clear batch
        outfit.Visible.Clear()

    let push outfit fleet (levels: int[]) position rotation flash =
        let sockets = mounts.[fleet.Active]
        let tint = 1.0 + flash * 0.5
        let fit family (batch: InstanceBatch) =
            for socket in sockets.[family] do
                let mounted = position + Quat.rotateVec3 rotation socket
                InstanceBatch.push batch mounted rotation Vec3.one tint tint tint (flash * 0.25)
            outfit.Visible.Add batch
        for family in 0 .. 2 do
            let rank = levels.[family]
            if rank > 0 then fit family outfit.Modules.[family].[rank - 1].Value
        if fleet.Kit = ShipCatalog.ShieldRelay then fit 3 outfit.Relay.Value
