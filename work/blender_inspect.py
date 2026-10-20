# Blender (headless): import a model and report objects, bones, animations, bounds, materials.
import bpy, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src = argv[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.fbx'):
    bpy.ops.import_scene.fbx(filepath=src)
else:
    bpy.ops.wm.obj_import(filepath=src)
for o in bpy.data.objects:
    extra = ''
    if o.type == 'MESH':
        pts = [o.matrix_world @ v.co for v in o.data.vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        extra = f'verts={len(o.data.vertices)} groups={len(o.vertex_groups)} bounds={tuple(round(c,3) for c in lo)}..{tuple(round(c,3) for c in hi)} mats={[m.name for m in o.data.materials]}'
    print('OBJ', o.type, o.name, 'parent=', o.parent.name if o.parent else None, extra)
for a in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
    mw = a.matrix_world
    def show(b, d):
        print('BONE', '  ' * d + b.name, tuple(round(c, 3) for c in (mw @ b.head_local)))
        for c in b.children: show(c, d + 1)
    for b in a.data.bones:
        if b.parent is None: show(b, 0)
for act in bpy.data.actions:
    print('ACTION', act.name, act.frame_range[:], len(act.fcurves))
