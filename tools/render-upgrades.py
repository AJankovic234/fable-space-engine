"""After npm run build: blender --background --python tools/render-upgrades.py"""
import json
import subprocess
from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parents[1]
out = root / "public" / "assets" / "upgrades"
out.mkdir(parents=True, exist_ok=True)

# Transfer the same indexed triangles, colours and emissions that WebGL draws.
extract = """
import { laser, shield, armour, relay } from './build/UpgradeMeshes.js';
const models = new Map([['relay', relay()]]);
for (const [family, build] of Object.entries({laser, shield, armour})) {
    for (const rank of [1, 2, 3]) models.set(family + '-' + rank, build(rank));
}
const output = {};
for (const [name, mesh] of models) {
    output[name] = [Array.from(mesh.Vertices), Array.from(mesh.Indices)];
}
console.log(JSON.stringify(output));
"""
models = json.loads(subprocess.check_output(
    ["node", "--input-type=module", "-e", extract], cwd=root))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 3
scene.render.resolution_x = 420
scene.render.resolution_y = 300
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "WEBP"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.quality = 94
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Medium High Contrast"
scene.world = bpy.data.worlds.new("Yard")
scene.world.use_nodes = True
sky = scene.world.node_tree.nodes["Background"]
sky.inputs["Color"].default_value = (0.70, 0.84, 1.0, 1.0)
sky.inputs["Strength"].default_value = 0.40


def point_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


for name, position, color, power, size in [
    ("Softbox", (-3, 4, 6), (1.0, 0.85, 0.67), 650, 5),
    ("Fill", (5, 3, 3), (0.76, 0.88, 1.0), 420, 4),
    ("Rim", (-2, -4, 4), (0.6, 0.84, 1.0), 760, 3),
]:
    lamp = bpy.data.lights.new(name, "AREA")
    lamp.energy, lamp.color, lamp.size = power, color, size
    obj = bpy.data.objects.new(name, lamp)
    scene.collection.objects.link(obj)
    obj.location = position
    point_at(obj, (0, 0, 0))

camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Yard orthographic"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.location = (7.6, 6.0, 6.45)
point_at(camera, (0, 0, 0))
rotation = camera.rotation_euler.to_quaternion()
right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
direction = camera.location.normalized()
frames, family_scale = {}, {}
materials = {}
for name, (buffer, indices) in models.items():
    vertices = [(buffer[i], -buffer[i + 2], buffer[i + 1]) for i in range(0, len(buffer), 10)]
    faces = [indices[i:i + 3] for i in range(0, len(indices), 3)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    slots, face_materials = {}, []
    for face in faces:
        offset = 10 * face[0] + 6
        colour = tuple(buffer[offset:offset + 4])
        if colour not in materials:
            material = bpy.data.materials.new("Finish")
            material.use_nodes = True
            surface = material.node_tree.nodes["Principled BSDF"]
            rgb = (*colour[:3], 1.0)
            surface.inputs["Base Color"].default_value = rgb
            surface.inputs["Metallic"].default_value = 0.40
            surface.inputs["Roughness"].default_value = 0.32
            surface.inputs["Emission Color"].default_value = rgb
            surface.inputs["Emission Strength"].default_value = colour[3]
            materials[colour] = material
        if colour not in slots:
            slots[colour] = len(mesh.materials)
            mesh.materials.append(materials[colour])
        face_materials.append(slots[colour])
    mesh.polygons.foreach_set("material_index", face_materials)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    obj.hide_render = True
    xs = [right.dot(v.co) for v in mesh.vertices]
    ys = [up.dot(v.co) for v in mesh.vertices]
    center = right * ((max(xs) + min(xs)) / 2) + up * ((max(ys) + min(ys)) / 2)
    scale = max((max(xs) - min(xs)) / 0.87, (max(ys) - min(ys)) * 1.4 / 0.86)
    family = name.split("-")[0]
    family_scale[family] = max(scale, family_scale.get(family, 0))
    frames[obj] = (center, family)

for obj, (center, family) in frames.items():
    camera.data.ortho_scale = family_scale[family]
    camera.location = direction * 12 + center
    obj.hide_render = False
    scene.render.filepath = str(out / (obj.name + ".webp"))
    bpy.ops.render.render(write_still=True)
    obj.hide_render = True
