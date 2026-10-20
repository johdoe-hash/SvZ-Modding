# Blender (headless): import a model and render front + side orthographic views (workbench, textured).
import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.obj'):
    bpy.ops.wm.obj_import(filepath=src)
elif src.lower().endswith('.glb'):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
c = (lo + hi) / 2; size = max(hi - lo)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 700; scene.render.resolution_y = 700
scene.render.film_transparent = False
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.1
for name, d in (('front', Vector((0, -1, 0))), ('side', Vector((1, 0, 0)))):
    cam.location = c + d * size * 3
    cam.rotation_euler = (d * -1).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = out + '_' + name + '.png'
    bpy.ops.render.render(write_still=True)
print('BOUNDS', tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi))
