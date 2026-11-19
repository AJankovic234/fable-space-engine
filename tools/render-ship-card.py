"""After npm run build:
blender --background artifacts/werft/eisvogel.blend --python tools/render-ship-card.py -- eisvogel [--save-scene]

Re-renders a shipyard card from the hull mesh the game draws. Camera, lights and colour
management come from the opened scene. With --save-scene the scene's own model of each
rendered hull is replaced by the game mesh and the .blend is saved, so a later render from
the scene cannot fall back to an outdated model.
"""
import colorsys
import json
import subprocess
import sys
from pathlib import Path

import bpy

root = Path(__file__).resolve().parents[1]
hulls = ["kutter", "lastkahn", "sperber", "eisvogel"]
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
save_scene = "--save-scene" in args
names = [arg for arg in args if not arg.startswith("--")] or ["eisvogel"]
# The scene's AgX view transform washes out saturated paint (the Eisvogel's azure turned sky grey).
# Lit surfaces get this much extra saturation so hull colours read as they do in the game.
PAINT_SATURATION = 1.4

# Transfer the same triangles, colours and emissions that WebGL draws.
extract = """
import { PlayerMeshes_models as models } from './build/PlayerMeshes.js';
const pick = JSON.parse(process.argv[1]);
console.log(JSON.stringify(pick.map(i => { const m = models[i](); return [Array.from(m.Vertices), Array.from(m.Indices)]; })));
"""
picked = [hulls.index(name) for name in names]
meshes = json.loads(subprocess.check_output(
    ["node", "--input-type=module", "-e", extract, json.dumps(picked)], cwd=root))

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


def material(key):
    r, g, b, glow = key
    if glow == 0:
        hue, saturation, value = colorsys.rgb_to_hsv(r, g, b)
        r, g, b = colorsys.hsv_to_rgb(hue, min(1.0, saturation * PAINT_SATURATION), value)
    mat = bpy.data.materials.new("card")
    mat.use_nodes = True
    shader = mat.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (r, g, b, 1.0)
    shader.inputs["Metallic"].default_value = 0.22
    shader.inputs["Roughness"].default_value = 0.38
    shader.inputs["Emission Color"].default_value = (r, g, b, 1.0)
    shader.inputs["Emission Strength"].default_value = glow * 4.0
    return mat


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
    scene.render.filepath = str(root / "public" / "assets" / "ships" / (name + ".webp"))
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
