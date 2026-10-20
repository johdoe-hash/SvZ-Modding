# Blender (headless): rig the static Sunflower Queen OBJ so the game's humanoid animations can drive it.
#   blender -b --python build_queen.py -- <queen.obj> <out.glb>
# Bones use humanoid names: Hips > Spine > Spine1 > Spine2 > Neck > Head up the stem (Head carries the
# whole flower), Left/Right Shoulder > Arm > ForeArm > Hand along the two leaf hands, and short
# Left/Right UpLeg > Leg > Foot > ToeBase "legs" in the root base. Rest pose roughly matches a T-pose
# (arms out sideways, legs down) so retargeted motion reads naturally. She faces -Y; her left is +X.
import bpy, sys
from mathutils import Vector
from mathutils.geometry import intersect_point_line

src, out = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=src)
body = next(o for o in bpy.data.objects if o.name.startswith('Body'))
head = next(o for o in bpy.data.objects if o.name.startswith('Head'))
for o in (body, head):
    o.data.transform(o.matrix_world)
    o.matrix_world.identity()
verts = [v.co.copy() for v in body.data.vertices]

def stem_y(z):
    near = [v.y for v in verts if abs(v.x) < 0.25 and abs(v.z - z) < 0.12]
    return sum(near) / len(near) if near else 0.0

# Her hands are the round knobs on twig arms just under the flower (the green leaves lower down are
# only leaves on the stem).
def hand(side):
    pts = [v for v in verts if side * v.x > 0.55 and 1.7 < v.z < 2.0]
    centre = sum(pts, Vector()) / len(pts)
    tip = max(pts, key=lambda v: side * v.x)
    return centre, tip

arm = bpy.data.objects.new('QueenRig', bpy.data.armatures.new('QueenRig'))
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones

def bone(name, h, t, parent=None):
    b = eb.new(name)
    b.head, b.tail = Vector(h), Vector(t)
    if parent:
        b.parent = eb[parent]
    return b

spine = [0.6, 0.9, 1.4, 1.9, 2.3, 2.75]
names = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck']
for i, n in enumerate(names):
    bone(n, (0, stem_y(spine[i]), spine[i]), (0, stem_y(spine[i + 1]), spine[i + 1]), names[i - 1] if i else None)
bone('Head', (0, stem_y(2.75), 2.75), (0, -0.1, 3.4), 'Neck')
for side, prefix in ((1, 'Left'), (-1, 'Right')):
    centre, tip = hand(side)
    z, y = centre.z, centre.y
    start = Vector((side * 0.22, y, z))
    elbow = start.lerp(tip, 0.45)
    wrist = start.lerp(tip, 0.8)
    bone(prefix + 'Shoulder', (side * 0.05, stem_y(z), z), start, 'Spine')
    bone(prefix + 'Arm', start, elbow, prefix + 'Shoulder')
    bone(prefix + 'ForeArm', elbow, wrist, prefix + 'Arm')
    bone(prefix + 'Hand', wrist, tip, prefix + 'ForeArm')
    bone(prefix + 'UpLeg', (side * 0.22, 0, 0.62), (side * 0.25, 0, 0.36), 'Hips')
    bone(prefix + 'Leg', (side * 0.25, 0, 0.36), (side * 0.27, 0, 0.12), prefix + 'UpLeg')
    bone(prefix + 'Foot', (side * 0.27, 0, 0.12), (side * 0.3, -0.25, 0.04), prefix + 'Leg')
    bone(prefix + 'ToeBase', (side * 0.3, -0.25, 0.04), (side * 0.3, -0.4, 0.02), prefix + 'Foot')
segments = {b.name: (b.head.copy(), b.tail.copy()) for b in eb}
bpy.ops.object.mode_set(mode='OBJECT')

def distance(p, seg):
    a, b = seg
    closest, t = intersect_point_line(p, a, b)
    closest = a if t < 0 else b if t > 1 else closest
    return (p - closest).length

def allowed(name, p):
    if 'Arm' in name or 'Hand' in name or 'Shoulder' in name:
        return abs(p.x) > 0.22 and 1.68 < p.z < 2.05 and p.y > 0.2
    if 'Leg' in name or 'Foot' in name or 'Toe' in name:
        return p.z < 0.8
    if name in ('Spine1', 'Spine2', 'Neck'):
        return p.z > 1.2
    return True

# Body: each vertex to its three nearest allowed bones (weight ~ 1/d^4); the stem top inside the
# flower and the whole head mesh go to Head only.
groups = {n: body.vertex_groups.new(name=n) for n in segments}
for v in body.data.vertices:
    p = v.co
    if p.z > 2.95:
        groups['Head'].add([v.index], 1.0, 'REPLACE')
        continue
    near = sorted((distance(p, s), n) for n, s in segments.items() if allowed(n, p))[:3]
    weights = [(1.0 / (d ** 4 + 1e-6), n) for d, n in near]
    total = sum(w for w, _ in weights)
    for w, n in weights:
        groups[n].add([v.index], w / total, 'REPLACE')
head.vertex_groups.new(name='Head').add(list(range(len(head.data.vertices))), 1.0, 'REPLACE')
for o in (body, head):
    o.parent = arm
    o.modifiers.new('Armature', 'ARMATURE').object = arm
print('BONES', {n: tuple(round(c, 2) for c in s[0]) for n, s in segments.items()})
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_yup=True, export_skins=True, export_animations=False)
print('EXPORTED', out)
