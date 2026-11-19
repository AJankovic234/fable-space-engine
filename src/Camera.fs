namespace SpaceEngine

// =====================================================================================
//  Camera.fs  -  Verfolgerkamera (Third Person) und Cockpit-Perspektive
//
//  Chase-Modus: Die Kamera folgt der Orientierung des Schiffs mit Verzoegerung
//  (Quat.slerp). Weil das Schiff sofort, die Kamera aber weich dreht, neigt und
//  rollt sich das Schiff sichtbar VOR der Kamera. Der Abstandsvektor wird
//  zusaetzlich per Vec3.lerp geglaettet; beim Boost faellt die Kamera zurueck
//  und der Oeffnungswinkel weitet sich.
//
//  Fahrtgefuehl (Camera.feel): Mit dem Tempo waechst der Abstand leicht und
//  verzoegert, in Gierkurven legt sich das Bild ein wenig mit dem Schiff.
//  Kamerawackeln ist eine kleine Drehung (Jolt), die nur in die Darstellung
//  eingeht - die Glaettung selbst bleibt davon unberuehrt.
//
//  Cockpit-Modus: starr mit dem Schiff verbunden.
// =====================================================================================

type CameraMode =
    | Chase
    | Cockpit

type Camera =
    { mutable Mode: CameraMode
      mutable Position: Vec3
      mutable Rotation: Quat
      /// Geglaetteter Abstand Kamera - Ziel in Weltkoordinaten.
      mutable Offset: Vec3
      /// Aktueller vertikaler Oeffnungswinkel (Radiant).
      mutable Fov: float
      mutable OpticsActive: bool
      View: Mat4
      Projection: Mat4
      // --- Abstimmung ---
      /// Kameraposition relativ zum Schiff im Chase-Modus (lokal: hinten oben).
      mutable ChaseOffset: Vec3
      /// Faktor auf den Verfolgerabstand (Bild auf/ab): 1 = Standard.
      mutable Zoom: float
      /// Kameraposition relativ zum Schiff im Cockpit-Modus.
      mutable CockpitOffset: Vec3
      /// Wie schnell die Kamera der Schiffsdrehung folgt (1/s; kleiner = traeger).
      mutable RotationStiffness: float
      /// Wie schnell der Abstandsvektor nachgefuehrt wird (1/s).
      mutable PositionStiffness: float
      mutable BaseFov: float
      /// Zusaetzlicher Oeffnungswinkel bei vollem Boost.
      mutable BoostFov: float
      /// Relative Abstandsvergroesserung bei vollem Boost.
      mutable BoostPullback: float
      mutable Near: float
      mutable Far: float
      /// Geglaettetes Vorwaertstempo relativ zum Reisetempo (0..1).
      mutable SpeedFeel: float
      /// Optische Querneigung der Verfolgerkamera in Gierkurven (Radiant, geglaettet).
      mutable Bank: float
      /// Kamerawackeln als kleine Drehung; wirkt nur auf View, nicht auf die Glaettung.
      mutable Jolt: Quat }

[<RequireQualifiedAccess>]
module Camera =

    /// Blick leicht nach unten: Das Schiff sitzt im unteren Drittel, ueber dem Radar,
    /// das Fadenkreuz knapp oberhalb der Bildmitte - nach vorn bleibt viel freie Sicht.
    [<Literal>]
    let ChaseTilt = -0.063

    [<Literal>]
    let OpticsMagnification = 1.8

    let steeringGain (camera: Camera) =
        if camera.OpticsActive then 1.0 / OpticsMagnification else 1.0

    let private targetFov (camera: Camera) boost =
        let wide = camera.BaseFov + camera.BoostFov * boost
        if camera.OpticsActive then 2.0 * atan (tan (wide * 0.5) / OpticsMagnification)
        else wide

    let private chaseTilt = Quat.fromAxisAngle Vec3.unitX ChaseTilt
    let private inverseChaseTilt = Quat.conjugate chaseTilt

    /// Relative Abstandsvergroesserung bei Reisetempo (verzoegert, s. SpeedLag).
    [<Literal>]
    let SpeedPullback = 0.08

    /// Zusaetzlicher Oeffnungswinkel bei Reisetempo (Radiant, ca. 3 Grad).
    [<Literal>]
    let SpeedFov = 0.052

    /// Wie schnell das Tempogefuehl dem echten Tempo folgt (1/s) - der Nachlauf.
    [<Literal>]
    let private SpeedLag = 1.8

    /// Querneigung der Kamera bei voller Gierrate (Radiant, ca. 4 Grad; das
    /// Schiffsmodell selbst legt sich um 0,2 rad).
    [<Literal>]
    let BankAngle = 0.07

    /// Wackelwinkel je Einheit Wackelstaerke (Radiant); die Staerke ist auf 2 begrenzt.
    [<Literal>]
    let ShakeAngle = 0.012

    let create () : Camera =
        let degrees = System.Math.PI / 180.0

        { Mode = Chase
          Position = Vec3.zero
          Rotation = Quat.identity
          Offset = Vec3.zero
          Fov = 60.0 * degrees
          OpticsActive = false
          View = Mat4.create ()
          Projection = Mat4.create ()
          ChaseOffset = Vec3(0.0, 4.4, 14.0)
          Zoom = 1.0
          CockpitOffset = Vec3(0.0, 0.52, -0.55)
          RotationStiffness = 7.0
          PositionStiffness = 10.0
          BaseFov = 60.0 * degrees
          BoostFov = 14.0 * degrees
          BoostPullback = 0.25
          Near = 0.3
          Far = 6000.0
          SpeedFeel = 0.0
          Bank = 0.0
          Jolt = Quat.identity }

    /// Wechselt zwischen Verfolger- und Cockpit-Perspektive. Der Uebergang ist
    /// weich, weil Offset und Rotation von ihrem aktuellen Wert aus weiterlaufen.
    let toggleMode (camera: Camera) =
        camera.Mode <-
            match camera.Mode with
            | Chase -> Cockpit
            | Cockpit -> Chase

    /// Abstandsfaktor der Verfolgerkamera aus Zoom, Boost und Tempogefuehl.
    let chaseDistanceFactor (camera: Camera) (boost: float) =
        (1.0 + camera.BoostPullback * boost) * (1.0 + SpeedPullback * camera.SpeedFeel) * camera.Zoom / steeringGain camera

    /// Setzt die Kamera ohne Glaettung hinter das Ziel (Start, Respawn).
    let snap (camera: Camera) (targetPosition: Vec3) (targetRotation: Quat) =
        let local =
            match camera.Mode with
            | Chase -> camera.ChaseOffset * chaseDistanceFactor camera 0.0
            | Cockpit -> camera.CockpitOffset

        camera.Rotation <- if camera.Mode = Chase then targetRotation * chaseTilt else targetRotation
        camera.Offset <- Quat.rotateVec3 targetRotation local
        camera.Position <- targetPosition + camera.Offset
        camera.Fov <- targetFov camera 0.0 + SpeedFov * camera.SpeedFeel
        camera.Bank <- 0.0
        camera.Jolt <- Quat.identity

    /// Fahrtgefuehl vor `update` aufrufen. `speed` = Vorwaertstempo / Reisetempo,
    /// `turn` = Gierrate / maximale Gierrate (positiv = Nase nach links).
    let feel (camera: Camera) (speed: float) (turn: float) (dt: float) =
        let s = System.Math.Max(0.0, System.Math.Min(1.0, speed))
        camera.SpeedFeel <- camera.SpeedFeel + (s - camera.SpeedFeel) * (1.0 - exp (-SpeedLag * dt))
        let bankTarget =
            if camera.Mode = Chase then System.Math.Max(-1.0, System.Math.Min(1.0, turn)) * BankAngle
            else 0.0
        camera.Bank <- camera.Bank + (bankTarget - camera.Bank) * (1.0 - exp (-4.0 * dt))

    /// Fuehrt die Kamera dem Ziel nach. `boost` in 0..1 steuert Rueckfall und FOV.
    /// Die Glaettungsfaktoren 1 - exp(-k * dt) sind bildratenunabhaengig.
    let update (camera: Camera) (targetPosition: Vec3) (targetRotation: Quat) (boost: float) (dt: float) =
        let fovTarget = targetFov camera boost + SpeedFov * camera.SpeedFeel
        camera.Fov <- camera.Fov + (fovTarget - camera.Fov) * (1.0 - exp (-5.0 * dt))

        match camera.Mode with
        | Chase ->
            camera.Rotation <-
                Quat.slerp camera.Rotation (targetRotation * chaseTilt) (1.0 - exp (-camera.RotationStiffness * dt))

            let desired = Quat.rotateVec3 (camera.Rotation * inverseChaseTilt) (camera.ChaseOffset * chaseDistanceFactor camera boost)
            camera.Offset <- Vec3.lerp camera.Offset desired (1.0 - exp (-camera.PositionStiffness * dt))
            camera.Position <- targetPosition + camera.Offset
        | Cockpit ->
            // Rotation starr, der Offset gleitet beim Umschalten weich ins Cockpit.
            camera.Rotation <- targetRotation
            let desired = Quat.rotateVec3 targetRotation camera.CockpitOffset
            let distanceSq = Vec3.distanceSq camera.Offset desired

            camera.Offset <-
                if distanceSq < 1e-4 then
                    desired
                else
                    Vec3.lerp camera.Offset desired (1.0 - exp (-18.0 * dt))

            camera.Position <- targetPosition + camera.Offset

    /// Kamerawackeln als kleine Drehung um alle drei Achsen. Mehrere unharmonische
    /// Sinus wirken wie Rauschen; je Achse hoechstens `min amount 2 * ShakeAngle`.
    let shakeRotation (amount: float) (time: float) : Quat =
        if amount <= 0.0 then
            Quat.identity
        else
            let a = System.Math.Min(amount, 2.0) * ShakeAngle
            let pitch = a * (0.6 * sin (time * 53.0) + 0.4 * sin (time * 97.0 + 1.3))
            let yaw = a * (0.6 * sin (time * 61.0 + 2.1) + 0.4 * sin (time * 89.0 + 0.7))
            let roll = a * 0.5 * sin (time * 71.0 + 0.4)
            Quat.fromAxisAngle Vec3.unitX pitch * Quat.fromAxisAngle Vec3.unitY yaw * Quat.fromAxisAngle Vec3.unitZ roll

    /// Die tatsaechlich dargestellte Blickrichtung: geglaettete Rotation plus
    /// Querneigung und Wackeln.
    let viewRotation (camera: Camera) : Quat =
        if camera.Bank = 0.0 then camera.Rotation * camera.Jolt
        else camera.Rotation * Quat.fromAxisAngle Vec3.unitZ camera.Bank * camera.Jolt

    /// Schreibt View- und Projektionsmatrix (einmal pro gerendertem Frame).
    let updateMatrices (camera: Camera) (aspect: float) =
        Mat4.viewFromRotationTranslation camera.View (viewRotation camera) camera.Position
        Mat4.perspective camera.Projection camera.Fov aspect camera.Near camera.Far
