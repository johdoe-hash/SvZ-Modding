# Blender (headless): build Zombot.glb for the SvZ mod loader from the Garden Warfare drone FBX.
#   blender -b --python build_zombot.py -- <drone.fbx> <diffuse.png> <out.glb>
# Keeps the highest-detail mesh, replaces the source rig (its bones and mesh disagree in scale) with a
# simple one - Root > Body > Muzzle - and keys the drone's own animations on Body:
#   Hover (idle), Fly (moving), Shoot (recoil), Crash (death), Spin (victory).
import bpy, math, sys
from mathutils import Vector, Matrix

src, diffuse, out = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
scene = bpy.context.scene
scene.render.fps = 30

# Bake the LOD0 meshes as displayed (armature applied) into plain world-space meshes.
depsgraph = bpy.context.evaluated_depsgraph_get()
baked = []
for o in [o for o in bpy.data.objects if o.type == 'MESH' and '.0' not in o.name]:
    mesh = bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph))
    mesh.transform(o.matrix_world)
    copy = bpy.data.objects.new(o.name, mesh)
    scene.collection.objects.link(copy)
    baked.append(copy)
for o in list(bpy.data.objects):
    if o not in baked:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in baked:
    o.select_set(True)
bpy.context.view_layer.objects.active = baked[0]
bpy.ops.object.join()
drone = bpy.context.view_layer.objects.active
drone.name = 'Zombot'

# One material with the drone's color texture.
mat = bpy.data.materials.new('Zombot')
mat.use_nodes = True
bsdf = mat.node_tree.nodes['Principled BSDF']
tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(diffuse)
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
drone.data.materials.clear()
drone.data.materials.append(mat)

# 100x (1.7 units tall), centered, resting on z = 0.
drone.data.transform(Matrix.Scale(100, 4))
vs = [v.co for v in drone.data.vertices]
lo = Vector([min(v[i] for v in vs) for i in range(3)])
hi = Vector([max(v[i] for v in vs) for i in range(3)])
drone.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
height = hi.z - lo.z
front = -(hi.y - lo.y) / 2          # the face points to -Y
print('DRONE', len(vs), 'verts, height', round(height, 3))

# Rig.
arm = bpy.data.objects.new('ZombotRig', bpy.data.armatures.new('ZombotRig'))
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
bones = arm.data.edit_bones
root = bones.new('Root'); root.head = (0, 0, 0); root.tail = (0, 0, height * 0.2)
body = bones.new('Body'); body.head = (0, 0, height * 0.5); body.tail = (0, 0, height * 0.8); body.parent = root
muzzle = bones.new('Muzzle'); muzzle.head = (0, front * 1.05, height * 0.22); muzzle.tail = (0, front * 1.05 - 0.2, height * 0.22); muzzle.parent = body
bpy.ops.object.mode_set(mode='OBJECT')
drone.parent = arm
group = drone.vertex_groups.new(name='Body')
group.add(list(range(len(drone.data.vertices))), 1.0, 'REPLACE')
drone.modifiers.new('Armature', 'ARMATURE').object = arm

# Animations. Body's local axes: Y = up, Z = forward (the face), X = sideways; so rotation X = pitch
# (positive dips the nose), Y = yaw, Z = roll.
arm.animation_data_create()
pose = arm.pose.bones['Body']
pose.rotation_mode = 'XYZ'

def action(name, frames, cyclic, keyframes):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    for frame, up, forward, pitch, yaw, roll in keyframes:
        pose.location = (0, up * height, forward * height)
        pose.rotation_euler = (math.radians(pitch), math.radians(yaw), math.radians(roll))
        pose.keyframe_insert('location', frame=frame)
        pose.keyframe_insert('rotation_euler', frame=frame)
    for curve in act.fcurves:
        if cyclic:
            curve.modifiers.new('CYCLES')
    act.frame_range = (1, frames)
    act.use_frame_range = True
    track = arm.animation_data.nla_tracks.new()
    track.name = name
    track.strips.new(name, 1, act)
    arm.animation_data.action = None

wave = lambda f, n, a, phase=0: a * math.sin(2 * math.pi * (f - 1) / n + phase)
action('Hover', 61, True, [(f, wave(f, 60, 0.05), 0, wave(f, 60, 3, 1.0), 0, wave(f, 60, 4, 0.5)) for f in range(1, 62, 5)])
action('Fly', 37, True, [(f, wave(f, 36, 0.035), 0, 16 + wave(f, 36, 3), wave(f, 36, 5, 1.2), wave(f, 36, 3, 0.4)) for f in range(1, 38, 3)])
action('Shoot', 25, False, [(1, 0, 0, 0, 0, 0), (3, 0.02, -0.14, -18, 0, 0), (7, 0.01, -0.1, -12, 0, 2), (14, 0, -0.04, -4, 0, -1), (25, 0, 0, 0, 0, 0)])
action('Crash', 46, False, [(1, 0, 0, 0, 0, 0), (8, 0.06, 0, -10, 40, 25), (18, 0, 0, 20, 140, -30), (30, -0.15, 0, 45, 250, 40), (46, -0.35, 0, 80, 330, 70)])
action('Spin', 41, True, [(f, wave(f, 40, 0.06), 0, 0, (f - 1) * 9, 0) for f in range(1, 42, 4)])

bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_yup=True, export_skins=True,
                          export_animations=True, export_animation_mode='NLA_TRACKS', export_force_sampling=True)
print('EXPORTED', out)
