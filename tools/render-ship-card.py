"""After npm run build:
blender --background artifacts/werft/eisvogel.blend --python tools/render-ship-card.py -- eisvogel [--save-scene]

Pass catalogue art names or kepler-bomber (with both mounted torpedoes) to select hulls.
Re-renders a shipyard card from the hull mesh the game draws. The camera and the positions of
the lights come from the opened scene; their strength, the surround and the exposure are set
below (YARD_LIGHT). With --save-scene the scene's own model of each rendered hull is replaced
by the game mesh and the .blend is saved, so a later render from the scene cannot fall back
to an outdated model.
"""
import colorsys
import json
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parents[1]
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
save_scene = "--save-scene" in args
names = [arg for arg in args if not arg.startswith("--")] or ["eisvogel"]
# The scene's AgX view transform washes out paint. Lit surfaces get this much extra saturation
# so the weathered hull colours read as they do in the game, without turning back into candy.
PAINT_SATURATION = 1.15

# The cards are lit like a dock at night, not like a showroom: a dark cold surround, one hard
# key, hardly any fill, a cool edge to cut the silhouette out of the panel and a sodium-vapour
# rim. All values are absolute, so re-running on a saved scene gives the same picture.
YARD_LIGHT = {
    "surround": ((0.20, 0.27, 0.36, 1.0), 0.10),
    "exposure": -0.55,
    "lights": {
        "Large warm key": (520.0, (1.0, 0.93, 0.84)),
        "Camera fill": (70.0, (0.73, 0.86, 1.0)),
        "Cool edge": (800.0, (0.62, 0.84, 1.0)),
        "Rim strip": (340.0, (1.0, 0.56, 0.22)),
    },
}

# Transfer the same triangles, colours and emissions that WebGL draws.
extract = """
import { PlayerMeshes_models as models, PlayerMeshes_bomber as bomber, PlayerMeshes_torpedo as torpedo } from './build/PlayerMeshes.js';
import { ShipCatalog_hulls as hulls } from './build/ShipCatalog.js';
import { ShipHardpoints_bomberRails as rails } from './build/ProceduralMeshes.js';
const pick = JSON.parse(process.argv[1]);
const byName = new Map(hulls.map((hull, i) => [hull.Art, models[i]]));
const extract = mesh => [Array.from(mesh.Vertices), Array.from(mesh.Indices)];
function card(name) {
    if (name !== 'kepler-bomber') {
        if (!byName.has(name)) throw new Error('Unknown ship card: ' + name);
        return extract(byName.get(name)());
    }
    const [vertices, indices] = extract(bomber());
    const payload = torpedo();
    for (const rail of rails) {
        const start = vertices.length / 10;
        const mounted = payload.Vertices.slice();
        for (let i = 0; i < mounted.length; i += 10) {
            mounted[i] += rail.X;
            mounted[i + 1] += rail.Y;
            mounted[i + 2] += rail.Z;
        }
        for (const value of mounted) vertices.push(value);
        for (const index of payload.Indices) indices.push(start + index);
    }
    return [vertices, indices];
}
console.log(JSON.stringify(pick.map(card)));
"""
meshes = json.loads(subprocess.check_output(
    ["node", "--input-type=module", "-e", extract, json.dumps(names)], cwd=root))

scene = bpy.context.scene
scene.render.resolution_x = 720
scene.render.resolution_y = 400
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "WEBP"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.quality = 90
for item in scene.objects:
    if item.type == "MESH":
        item.hide_render = True
    elif item.type == "LIGHT" and item.name in YARD_LIGHT["lights"]:
        item.data.energy, item.data.color = YARD_LIGHT["lights"][item.name]
scene.view_settings.exposure = YARD_LIGHT["exposure"]
if scene.world and scene.world.use_nodes:
    for node in scene.world.node_tree.nodes:
        if node.type == "BACKGROUND":
            node.inputs[0].default_value, node.inputs[1].default_value = YARD_LIGHT["surround"]


def material(key):
    r, g, b, channel = key
    # PlayerMeshes packs metal, opaque canopy glass and rubber into the negative
    # part of the emissive channel (Materials.fs). Its fraction is still light.
    surface = int(-channel) if channel < 0 else 0
    glow = -channel - surface if channel < 0 else channel
    if surface == 0 and glow == 0:
        hue, saturation, value = colorsys.rgb_to_hsv(r, g, b)
        r, g, b = colorsys.hsv_to_rgb(hue, min(1.0, saturation * PAINT_SATURATION), value)
    mat = bpy.data.materials.new("card")
    mat.use_nodes = True
    shader = mat.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (r, g, b, 1.0)
    shader.inputs["Metallic"].default_value = (0.12, 0.90, 0.15, 0.0)[surface]
    shader.inputs["Roughness"].default_value = (0.54, 0.42, 0.16, 0.83)[surface]
    if surface == 2:
        shader.inputs["Coat Weight"].default_value = 0.50
    shader.inputs["Emission Color"].default_value = (r, g, b, 1.0)
    shader.inputs["Emission Strength"].default_value = glow * 4.0
    return mat


def frame_hull(camera, points):
    """Fit the model silhouette with the same margin on every landscape card."""
    bpy.context.view_layer.update()
    view = camera.matrix_world.inverted()
    projected = [view @ Vector(p) for p in points]
    left, right = min(p.x for p in projected), max(p.x for p in projected)
    bottom, top = min(p.y for p in projected), max(p.y for p in projected)
    centre = Vector(((left + right) / 2, (bottom + top) / 2, 0))
    camera.location += camera.matrix_world.to_quaternion() @ centre
    aspect = scene.render.resolution_x / scene.render.resolution_y
    camera.data.ortho_scale = max(right - left, (top - bottom) * aspect) / 0.89


for name, (vertices, indices) in zip(names, meshes):
    # Vertex layout: position, normal, colour, emissive. Game Y is up and the bow points to -Z.
    points = [(vertices[i], -vertices[i + 2], vertices[i + 1]) for i in range(0, len(vertices), 10)]
    faces = [tuple(indices[i:i + 3]) for i in range(0, len(indices), 3)]
    data = bpy.data.meshes.new(name + "-card")
    data.from_pydata(points, [], faces)
    slots = {}
    for polygon in data.polygons:
        at = polygon.vertices[0] * 10
        key = tuple(round(value, 3) for value in vertices[at + 6:at + 10])
        if key not in slots:
            slots[key] = len(slots)
            data.materials.append(material(key))
        polygon.material_index = slots[key]
    model = bpy.data.objects.new(name + "-card", data)
    scene.collection.objects.link(model)
    frame_hull(scene.camera, points)
    scene.render.filepath = bpy.path.relpath(str(root / "public" / "assets" / "ships" / (name + ".webp")))
    bpy.ops.render.render(write_still=True)
    model.hide_render = True

if save_scene:
    for name in names:
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
        model = bpy.data.objects[name + "-card"]
        model.name = model.data.name = name
        model.hide_render = False
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backup next to the repo file
    bpy.ops.wm.save_mainfile()
