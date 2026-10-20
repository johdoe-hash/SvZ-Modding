# Blender (headless): import a rigged GLB, pose some bones, render front/side views.
import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out, poses = argv[0], argv[1], argv[2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for spec in poses:                       # Bone:axis:degrees
    name, axis, deg = spec.split(':')
    pb = arm.pose.bones[name]; pb.rotation_mode = 'XYZ'
    r = list(pb.rotation_euler); r['xyz'.index(axis)] += math.radians(float(deg)); pb.rotation_euler = r
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = []
for o in [o for o in bpy.data.objects if o.type == 'MESH']:
    m = o.evaluated_get(dg).to_mesh(); pts += [o.matrix_world @ v.co for v in m.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
c = (lo + hi) / 2; size = max(hi - lo)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = scene.render.resolution_y = 600
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.15
for name, d in (('front', Vector((0, -1, 0))), ('side', Vector((1, 0, 0)))):
    cam.location = c + d * size * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = out + '_' + name + '.png'; bpy.ops.render.render(write_still=True)
