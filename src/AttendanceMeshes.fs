namespace SpaceEngine

/// DAA-Kreuzer in Metern. Die Registratur sitzt auf einem breiten Aktenrumpf,
/// die Hauptkamera an einem eigenen Drehjoch vor der Steuerbordschulter.
[<RequireQualifiedAccess>]
module AttendanceMeshes =
    let private v x y z = Vec3(x,y,z)
    let private petrol = MeshColor.rgb 0.052 0.165 0.162
    let private faded = MeshColor.rgb 0.15 0.24 0.22
    let private cream = MeshColor.rgb 0.55 0.51 0.385
    let private paper = MeshColor.rgb 0.59 0.558 0.43
    let private ink = MeshColor.rgb 0.040 0.057 0.057
    let private rubber = MeshColor.rubber 0.033 0.038 0.038
    let private iron = MeshColor.metal 0.17 0.185 0.18
    let private nickel = MeshColor.metal 0.38 0.39 0.34
    let private bronze = MeshColor.metal 0.31 0.19 0.095
    let private glass = MeshColor.glass 0.033 0.063 0.062 0.10
    let private office = MeshColor.glow 0.88 0.70 0.40 0.46
    let private amber = MeshColor.glow 1.0 0.43 0.065 0.85
    let private aperture = MeshColor.glow 0.56 0.95 0.78 0.90

    let sensor = v 17.2 4.6 -46.0
    let drives = [| v -9.3 -1.1 46.0,3.10; v 0.0 -2.3 50.0,3.82; v 9.3 -1.1 46.0,3.10 |]
    let lamps = [| v -15.85 2.10 18.1; v 15.85 2.10 18.1 |]
    let radius = 56.5
    let pivot = v -3.6 -10.8 -24.8
    let headLens = v 0.0 0.0 -3.65

    let private hull (b: MeshBuilder) x profiles deck cheek belt bottom =
        let sections = profiles |> Array.map (fun (z,w,lo,hi) -> ProceduralMeshes.hullSection x z w lo hi)
        let center (r: Vec3[]) = (r.[0] + r.[4])*0.5
        let paint = [|deck; cheek; belt; bottom; bottom; bottom; belt; cheek|]
        for a,c in Array.pairwise sections do
            let inside = (center a + center c)*0.5
            for i in 0..7 do
                let j = (i+1)%8
                b.PolygonOutward([|a.[i];a.[j];c.[j];c.[i]|],inside,paint.[i])
        b.PolygonOutward(sections.[0],center sections.[1],belt)
        b.PolygonOutward(Array.last sections,center sections.[sections.Length-2],belt)

    /// Die Beschriftung besteht aus breiten Schablonenstrichen mit freiem Innenraum.
    let private registry (b: MeshBuilder) (origin: Vec3) (across: Vec3) (up: Vec3) (normal: Vec3) =
        let print points =
            b.PolygonOutward(points |> Array.map (fun (x,y) -> origin + across*x + up*y),origin-normal,ink)
        print [|0.,0.; 0.15,0.; 0.15,1.; 0.,1.|]
        print [|0.15,0.82; 0.48,0.82; 0.55,1.; 0.15,1.|]
        print [|0.48,0.82; 0.63,0.72; 0.79,0.80; 0.55,1.|]
        print [|0.63,0.28; 0.79,0.20; 0.79,0.80; 0.63,0.72|]
        print [|0.55,0.; 0.79,0.20; 0.63,0.28; 0.48,0.18|]
        print [|0.15,0.; 0.55,0.; 0.48,0.18; 0.15,0.18|]
        print [|0.90,0.; 1.03,0.; 1.36,1.; 1.23,1.|]
        for x in [1.42;2.32] do
            print [|x,0.; x+0.18,0.; x+0.40,0.75; x+0.40,1.; x+0.32,1.|]
            print [|x+0.40,1.; x+0.48,1.; x+0.80,0.; x+0.62,0.; x+0.40,0.75|]
            print [|x+0.268,0.30; x+0.532,0.30; x+0.488,0.45; x+0.312,0.45|]

    /// Der Mittelpunkt ist die physische Pupille. Blendenring und Sonnenblende
    /// stehen davor, die schrägen Irislamellen liegen einzeln über dem Rauchglas.
    let private camera (b: MeshBuilder) center (axis: Vec3) r =
        let across = Vec3.normalize (Vec3.cross Vec3.unitY axis)
        let up = Vec3.cross axis across
        let at depth x y = center + axis*(depth*r) + across*(x*r) + up*(y*r)
        let ring depth scale =
            ProceduralMeshes.ring(16,0.,0.,0.,scale,scale)
            |> Array.map (fun p -> at depth p.X p.Y)
        let back, tube, focus, rim = ring -1.68 0.73, ring -1.26 0.97, ring -0.57 0.90, ring 0.23 1.0
        b.Loft([|back;tube;focus|],true,false,iron)
        b.Loft([|focus;rim|],false,false,petrol)
        b.Loft([|ring -1.10 0.97;ring -0.96 0.96|],false,false,bronze)
        let lip, well, iris, pupil = ring 0.23 0.84, ring -0.23 0.76, ring -0.12 0.40, ring -0.02 0.19
        for i in 0..15 do
            let j = (i+1)%16
            let band (a: Vec3[]) (c: Vec3[]) inside color =
                b.PolygonOutward([|a.[i]; a.[j]; c.[j]; c.[i]|],inside,color)
            band rim lip (center-axis*r) nickel
            band lip well rim.[i] rubber
            band well iris (center-axis*r) (if i >= 2 && i <= 5 then faded else glass)
            let k = (i+3)%16
            b.PolygonOutward([|iris.[i];iris.[j];pupil.[(k+1)%16];pupil.[k]|],center-axis*r,
                             (if i%4=0 then bronze else ink))
            b.TriangleOutward(pupil.[i],pupil.[j],center,center-axis*r,aperture)
        // Ein halbes Gehäuse über dem Objektiv ist aus der Seitenansicht als Visier lesbar.
        let outline = [|at -1.30 -0.76 0.84; at -1.30 0.76 0.84; at 0.54 0.83 0.99; at 0.54 -0.83 0.99|]
        b.Slab(outline,Array.create 4 (0.074*r),up,cream)

    /// Bewegliche Bauchkamera im lokalen System ihres Kugelgelenks.
    let head () : MeshData =
        let b = MeshBuilder()
        let ring z r = ProceduralMeshes.ring(12,0.,0.,z,r,r)
        b.Loft([|ring -1.85 1.64;ring -0.65 2.60;ring 0.65 2.60;ring 2.12 1.62;ring 2.55 0.74|],
               true,true,petrol)
        b.Loft([|ring 0.15 2.70;ring 0.55 2.70|],false,false,bronze)
        camera b headLens (-Vec3.unitZ) 2.15
        b.Build()

    let private nozzle (b: MeshBuilder) (exit: Vec3) r =
        let ring dz size = ProceduralMeshes.ring(14,exit.X,exit.Y,exit.Z+dz*r,size*r,size*r)
        let neck, lip, inside, throat = ring -1.8 0.84, ring 0.0 1.0, ring 0.0 0.79, ring -0.58 0.52
        b.Loft([|neck;ring -0.82 0.88;lip|],true,false,iron)
        for i in 0..13 do
            let j = (i+1)%14
            b.PolygonOutward([|lip.[i];lip.[j];inside.[j];inside.[i]|],exit-Vec3.unitZ,nickel)
            b.PolygonOutward([|inside.[i];inside.[j];throat.[j];throat.[i]|],lip.[i],bronze)
        b.PolygonOutward(throat,exit-Vec3.unitZ*r,MeshColor.glow 0.70 0.88 0.75 1.0)

    /// Ein Zweiziffern-Aufruf wie an einem Amtsschalter, auf beiden Flanken lesbar.
    let private queueNumber (b: MeshBuilder) side =
        let normal = Vec3.unitX*side
        let point u y = v (side*15.95) y (-3.2-side*u)
        b.Box(point 0.0 2.4,v 0.29 2.2 5.25,ink)
        let stroke x0 y0 x1 y1 =
            let at x y = point x y + normal*0.305
            b.PolygonOutward([|at x0 y0; at x1 y0; at x1 y1; at x0 y1|],point 0.0 2.4,amber)
        // 47
        stroke -3.65 2.30 -3.21 3.98
        stroke -3.18 2.26 -1.43 2.66
        stroke -1.40 0.82 -0.96 3.98
        stroke 0.93 3.62 3.53 4.02
        stroke 3.10 0.82 3.55 3.59

    let cruiser () : MeshData =
        let b = MeshBuilder()
        let beam = ProceduralMeshes.beam b
        let box x y z hx hy hz color = b.Box(v x y z,v hx hy hz,color)
        // Stumpfer Ablagebug, tiefer Kiel, gestufter Heckspiegel.
        hull b 0.0 [|-50.5,3.3,-3.2,1.0; -43.,7.8,-5.6,3.7; -25.,11.2,-6.7,4.3
                     -10.,13.4,-7.2,4.9; 19.,13.4,-7.2,4.9; 30.,11.9,-6.1,4.1; 40.2,8.4,-4.4,2.4|]
            faded petrol ink iron
        hull b 0.0 [|-48.8,2.8,0.3,1.4; -41.,6.3,1.2,4.6; -26.5,8.8,2.7,5.5; -16.,8.8,3.3,6.1|]
            cream cream faded petrol
        hull b 0.0 [|33.4,5.8,-5.4,3.3;40.8,5.2,-6.6,2.4;45.9,3.6,-5.8,1.2|]
            faded iron petrol ink
        // Festes Lager für den unabhängig schwenkenden Kopf unter dem Vorschiff.
        let bearing dy r =
            ProceduralMeshes.ring(12,0.,0.,0.,r,r)
            |> Array.map (fun p -> pivot+v p.X dy p.Y)
        b.Loft([|bearing 5.1 3.5;bearing 2.5 2.45;bearing 1.6 1.65;bearing 0.15 1.3|],true,true,iron)
        b.Loft([|bearing 2.5 2.52;bearing 2.1 2.16|],false,false,cream)
        // Der angeschraubte Gegenleser unter dem Bug hat seitliches Sichtfeld.
        hull b -9.3 [|-27.,3.1,-3.6,0.8; -23.,5.4,-4.2,2.3; -14.,4.9,-3.9,1.6|]
            faded iron petrol ink
        let sideLook = Vec3.normalize (v -0.91 -0.06 -0.40)
        camera b (v -18.0 -1.5 -24.0) sideLook 4.25

        // Offenes Drehjoch: zwei Lastpfade um das Gehäuse, große Lagerkappe außen.
        for z in [-32.0;-23.0] do
            beam (v 6.2 1.0 z) (v 16.0 2.2 (z-2.0)) 1.48 iron
            beam (v 7.2 -3.6 (z+2.2)) (v 16.0 2.2 (z-2.0)) 0.81 faded
        hull b 17.2 [|-38.7,5.0,-1.7,9.8; -35.8,6.6,-2.5,11.0; -25.8,6.8,-2.5,9.6; -21.8,4.4,0.1,7.1|]
            cream faded petrol rubber
        let pivot x scale =
            ProceduralMeshes.ring(12,0.,0.,0.,scale,scale)
            |> Array.map (fun p -> v x (3.8+p.Y) (-29.6+p.X))
        b.Loft([|pivot 22.0 4.05;pivot 24.7 4.05;pivot 25.2 3.28|],true,true,iron)
        b.Loft([|pivot 25.18 2.51;pivot 25.45 2.31|],false,true,cream)
        camera b sensor (-Vec3.unitZ) 7.1

        // Zwei Reihen Archivschubladen stehen über dem Kiel. Zwischen den Kästen
        // sieht man Tragschienen, zurückgesetzte Fenster und die Auszüge darunter.
        for side in [-1.;1.] do
            let sx x = side*x
            hull b (sx 12.3) [|-16.,2.3,-3.4,3.7; -11.7,3.4,-4.2,5.8; 19.0,3.4,-4.2,5.8; 25.3,2.0,-2.7,4.2|]
                faded cream petrol iron
            for row in 0..3 do
                let z = -11.2 + 8.25*float row
                box (sx 15.76) -1.1 z 0.26 1.13 3.66 ink
                box (sx 16.05) -1.0 z 0.10 0.84 3.39 (if row=1 then faded else petrol)
                box (sx 16.19) -0.80 z 0.07 0.16 1.14 nickel
            queueNumber b side
            let signage = v (sx 15.9) 0.78 12.0
            b.Box(signage,v 0.22 2.02 5.02,cream)
            registry b (signage + v (sx 0.245) -1.19 (side*4.24)) (v 0.0 0.0 (-side*2.69))
                (Vec3.unitY*2.44) (Vec3.unitX*side)
            hull b (sx 9.3) [|25.2,2.6,-4.5,2.8; 28.3,4.4,-5.4,3.6; 38.8,4.0,-5.2,2.9; 42.8,3.0,-4.0,1.7|]
                petrol faded iron ink
            for z in [29.5;32.1;34.7;37.3] do box (sx 9.3) 3.54 z 2.2 0.35 0.39 iron
            let lamp = lamps.[if side < 0.0 then 0 else 1]
            b.Box(lamp-v (sx 0.16) 0.0 0.0,v 0.34 0.49 0.81,ink)
            b.Box(lamp+v (sx 0.18) 0.0 0.0,v 0.21 0.33 0.63,
                  (if side < 0.0 then MeshColor.glow 1. 0.10 0.04 0.88 else MeshColor.glow 0.10 0.85 0.36 0.88))

        // Staffelgeschoss der Registratur. Licht liegt hinter den breiten Brüstungen,
        // einzelne Büros sind dunkel. Auf dem Dach ist derselbe Registraturstempel.
        for floor in 0..1 do
            let y = 4.5 + 4.3*float floor
            let w = 10.1 - 2.4*float floor
            let first, last = -14.0+3.0*float floor, 19.0-3.0*float floor
            hull b 0.0 [|first,w-1.4,y-0.38,y+1.3;first+2.2,w,y-0.6,y+3.75
                         last-2.4,w,y-0.6,y+3.75;last,w-1.3,y-0.5,y+2.2|] cream faded glass ink
            for side in [-1.;1.] do
                for bay in 0..5 do
                    let z = first + 3.20 + (last-first-6.40)*float bay/5.0
                    if (bay+floor+(if side < 0.0 then 1 else 0))%4<>0 then
                        box (side*(w+0.045)) (y+1.65) z 0.08 0.61 1.17 office
                box (side*(w+0.11)) (y+2.92) ((first+last)*0.5) 0.34 0.15 ((last-first)*0.43) petrol
        box 0.0 12.68 2.2 4.25 0.15 7.6 paper
        registry b (v -3.05 12.86 -4.42) (Vec3.unitZ*4.21) (Vec3.unitX*6.1) Vec3.unitY
        for x in [-4.75;4.75] do
            beam (v x 11.9 -7.9) (v x 16.0 -9.6) 0.31 iron
        beam (v -4.85 16.0 -9.6) (v 4.85 16.0 -9.6) 0.42 petrol
        for drive,r in drives do nozzle b drive r
        b.Build()
