# Print a ripped Unity prefab's transform hierarchy (world positions) and its Animation clips.
#   python prefab_tree.py <ripped project>/Assets/Resources/characters/PFNobunaga.prefab [--all]
import re, sys, os, glob
import numpy as np
path, full = sys.argv[1], '--all' in sys.argv
# Clip names come from the .anim.meta files of the ripped project the prefab is in.
ROOT = os.path.abspath(path)
while os.path.basename(ROOT) != 'Assets' and os.path.dirname(ROOT) != ROOT:
    ROOT = os.path.dirname(ROOT)
s = open(path, encoding='utf-8').read()
docs = re.split(r'^--- !u!(\d+) &(\d+)', s, flags=re.M)
objs = {docs[i+1]: (docs[i], docs[i+2]) for i in range(1, len(docs), 3)}
names = {fid: re.search(r'm_Name: (.*)', b).group(1) for fid, (t, b) in objs.items() if t == '1'}
tr = {}
for fid, (t, b) in objs.items():
    if t == '4':
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', b).group(1)
        father = re.search(r'm_Father: \{fileID: (\d+)\}', b).group(1)
        f3 = lambda k: tuple(map(float, re.search(k + r': \{x: ([^,]+), y: ([^,]+), z: ([^,}]+)', b).groups()))
        rot = tuple(map(float, re.search(r'm_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}', b).groups()))
        tr[fid] = dict(name=names.get(go, '?'), go=go, father=father, pos=f3('m_LocalPosition'), rot=rot, scale=f3('m_LocalScale'))
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx, aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz)
def qrot(q, v):
    x, y, z, w = q; u = np.array([x, y, z]); v = np.array(v)
    return 2*np.dot(u, v)*u + (w*w-np.dot(u, u))*v + 2*w*np.cross(u, v)
W = {}
def world(fid):
    if fid in W: return W[fid]
    t = tr[fid]
    if t['father'] not in tr:
        W[fid] = (np.zeros(3), (0, 0, 0, 1), np.ones(3))   # prefab root at origin
    else:
        pp, pr, ps = world(t['father'])
        W[fid] = (pp + qrot(pr, np.array(t['pos']) * ps), qmul(pr, t['rot']), ps * np.array(t['scale']))
    return W[fid]
kids = {}
for fid, t in tr.items(): kids.setdefault(t['father'], []).append(fid)
skip = re.compile(r'rig_grp|_ctrl|Ctrl|space_grp|_grp$|IK|ik_|pole', re.I)
def show(fid, d):
    t = tr[fid]; p = world(fid)[0]
    if full or not skip.search(t['name']) or d < 2:
        print('  '*d + f"{t['name']}  ({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f})" + (f"  scale={t['scale']}" if t['scale'] != (1.0, 1.0, 1.0) else ''))
    for k in kids.get(fid, []): show(k, d+1)
for fid, t in tr.items():
    if t['father'] not in tr: show(fid, 0)
guids = {}
for meta in glob.glob(ROOT + '/**/*.anim.meta', recursive=True):
    g = re.search(r'guid: (\w+)', open(meta, encoding='utf-8').read()).group(1)
    guids[g] = os.path.basename(meta)[:-10]
for fid, (t, b) in objs.items():
    if t == '111':
        clips = re.findall(r'guid: (\w+)', b.split('m_Animations:')[1].split('m_WrapMode')[0]) if 'm_Animations:' in b else []
        print('ANIMATION on', names.get(re.search(r'm_GameObject: \{fileID: (\d+)\}', b).group(1)), ':', sorted(set(guids.get(g, g[:8]) for g in clips)))
smr = sum(1 for t, b in objs.values() if t == '137'); print('SkinnedMeshRenderers:', smr, ' MeshRenderers:', sum(1 for t, b in objs.values() if t == '23'))
