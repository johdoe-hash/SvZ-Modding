# Blender (headless): import the Crazy Dave FBX, report its skeleton, export a GLB for the mod loader.
import bpy, sys, os
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for o in bpy.data.objects:
    print('OBJ', o.type, o.name, 'parent=', o.parent.name if o.parent else None, 'verts=', len(o.data.vertices) if o.type == 'MESH' else '', 'scale=', tuple(round(v, 4) for v in o.scale))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mw = arm.matrix_world
def show(b, d):
    h = mw @ b.head_local
    print('BONE', '  ' * d + b.name, '(%.3f, %.3f, %.3f)' % tuple(h))
    for c in b.children: show(c, d + 1)
for b in arm.data.bones:
    if b.parent is None: show(b, 0)
for m in bpy.data.materials:
    texs = [n.image.name for n in (m.node_tree.nodes if m.use_nodes else []) if n.type == 'TEX_IMAGE' and n.image]
    print('MAT', m.name, texs)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=False, export_skins=True, export_yup=True, export_apply=False)
print('EXPORTED', out, os.path.getsize(out))
