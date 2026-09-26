"""Quick look at the helicopter body + interior without Unity: builds both, renders a few views with
Blender's Workbench (flat material colours) to the scratch folder.
  /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/preview_body.py -- <out_dir>
"""
import bpy, sys, os, math
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "/tmp"
os.makedirs(out, exist_ok=True)
root = "/Users/samwudel/Game/tools/blender/"
# body first (it clears the scene), then the interior WITHOUT clearing (patch its build() clear-out)
exec(open(root + "build_body.py").read())
src = open(root + "build_interior.py").read()
src = src.replace("    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)\n    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)\n    GROUPS.clear()\n", "    GROUPS.clear()\n")
src = src.replace("OUT = ", "OUT_INTERIOR_UNUSED = ").replace("bpy.ops.export_scene.fbx(filepath=OUT,", "bpy.ops.export_scene.fbx(filepath='/tmp/_interior_preview.fbx',")
exec(src)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 1400, 900
scene.render.film_transparent = False
world = bpy.data.worlds.new("W") if not bpy.data.worlds else bpy.data.worlds[0]
scene.world = world
world.color = (0.55, 0.5, 0.45)
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
cam_data.lens = 40

def to_b(v): return Vector((-v[0], -v[2], v[1]))
def shoot(name, pos, look, lens=40):
    cam_data.lens = lens
    cam.location = to_b(pos)
    d = to_b(look) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

hide = {o.name: o.hide_render for o in bpy.data.objects}
shoot("front_quarter", (7, 3.5, 8.5), (0, 1.7, 0.6))
shoot("side", (11, 2.2, 0.5), (0, 1.7, 0.2))
shoot("rear_quarter", (-7, 3.5, -8), (0, 1.9, -1.0))
shoot("top", (0.5, 14, 0.4), (0, 1.7, 0.3))
shoot("low_front", (-5, 0.9, 6), (0, 1.4, 1.0), 35)
# door detail and cabin (hide the right hull side? no: look in through the open right door by hiding Door_R)
for o in bpy.data.objects:
    if o.name.startswith("Door_R") or (o.parent and o.parent.name == "Door_R"): o.hide_render = True
shoot("door_open_view", (4.2, 2.0, 0.6), (0, 1.6, 0.5), 32)
shoot("cabin_inside", (0.55, 1.75, 0.55), (-0.6, 1.4, 1.6), 22)
shoot("cockpit_inside", (0.45, 1.98, 1.95), (0.3, 1.7, 3.0), 22)
for o in bpy.data.objects: o.hide_render = hide.get(o.name, False)
print("[preview] done")
