"""
Builds the medevac helicopter BODY (everything outside the cabin: hull, lining, doors on rails, glass,
rotors, tail, skids, engine) and exports it to Unity. The cockpit/cabin interior comes from
build_interior.py; the two are authored in the same UNITY helicopter-local coordinates (x right,
y up, z forward, metres; floor y=0.95, cabin z -0.5..1.45, cockpit z 1.45..3.35) so they fit by construction.
Target look: ArtSource/Helicopters/Medevac/medevac_reference.png (stubby red medevac, tape patches).

Run headless:  /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/build_body.py

Output: RescueGame/Assets/Art/Helicopters/Medevac/Body/Body.fbx
  Hull (outer skin) / Lining (inner surface, what you see from inside) / Jambs / Glass /
  Door_L, Door_R (pivot at their closed position, slide along -z) / Rails /
  MainRotor (pivot at the hub, spins about Y) / TailRotor (pivot at its hub, spins about X) /
  Tail / Skids / Engine / Details, plus markers M_*.
Materials are named Mat_<Name>; Unity maps them to Materials/Interior/Mat_<Name>.mat.
"""
import bpy, bmesh, math, os
from mathutils import Matrix, Vector

OUT = "/Users/samwudel/Game/RescueGame/Assets/Art/Helicopters/Medevac/Body/Body.fbx"

PALETTE = {
    "HullRed": (0.74, 0.17, 0.11), "PadCream": (0.86, 0.80, 0.66), "PadRed": (0.70, 0.24, 0.18),
    "Grey": (0.45, 0.45, 0.47), "DarkGrey": (0.17, 0.17, 0.19), "Black": (0.06, 0.06, 0.07),
    "Metal": (0.62, 0.63, 0.66), "Tape": (0.68, 0.68, 0.64), "Yellow": (0.96, 0.74, 0.12),
    "White": (0.92, 0.92, 0.90), "Glass": (0.3, 0.4, 0.5), "Rubber": (0.12, 0.12, 0.12),
    "RedKnob": (0.85, 0.10, 0.08), "Olive": (0.36, 0.42, 0.30),
    "HullRedDark": (0.55, 0.12, 0.09), "Primer": (0.52, 0.5, 0.46), "Soot": (0.12, 0.1, 0.09), "Cardboard": (0.62, 0.48, 0.3),
}

def mat(name):
    m = bpy.data.materials.get("Mat_" + name)
    if m is None:
        m = bpy.data.materials.new("Mat_" + name)
        c = PALETTE.get(name, (1, 0, 1))
        m.diffuse_color = (*c, 1)
    return m

def to_b(v):  # Unity local -> Blender
    return Vector((-v[0], -v[2], v[1]))

GROUPS = {}
def group(name, parent=None):
    if name in GROUPS: return GROUPS[name]
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    if parent: e.parent = group(parent)
    GROUPS[name] = e
    return e

def unity_rot(rx=0, ry=0, rz=0):
    return (Matrix.Rotation(math.radians(ry), 4, 'Y') @ Matrix.Rotation(math.radians(rx), 4, 'X')
            @ Matrix.Rotation(math.radians(rz), 4, 'Z'))

def finish(bm, name, material, grp, pivot=(0, 0, 0), bevel=0.012):
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', clamp_overlap=True)
    for v in bm.verts:
        v.co = to_b(Vector(v.co) - Vector(pivot))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    me.materials.append(mat(material))
    for p in me.polygons: p.use_smooth = False
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = group(grp)
    return o

def box(name, center, size, material, grp, rot=(0, 0, 0), bevel=0.012, pivot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Translation(Vector(center)) @ unity_rot(*rot) @ Matrix.Diagonal(Vector((*size, 1)))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return finish(bm, name, material, grp, pivot, min(bevel, min(size) * 0.3))

def cyl(name, center, radius, length, material, grp, rot=(0, 0, 0), segs=10, bevel=0.006, pivot=(0, 0, 0), r2=None):
    """Cylinder (or cone if r2 given) along its local Y axis (Unity up) before rotation."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius if r2 is None else r2, depth=length)
    bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(-90), 4, 'X'), verts=bm.verts)
    m = Matrix.Translation(Vector(center)) @ unity_rot(*rot)
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return finish(bm, name, material, grp, pivot, min(bevel, radius * 0.3))

def tube(name, pts, radius, material, grp, segs=6, pivot=(0, 0, 0)):
    objs = []
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        d = b - a; L = d.length
        if L < 1e-4: continue
        q = Vector((0, 1, 0)).rotation_difference(d.normalized())
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=L + radius * 1.2)
        bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(-90), 4, 'X'), verts=bm.verts)
        bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4(), verts=bm.verts)
        objs.append(finish(bm, f"{name}_{i}", material, grp, pivot=pivot, bevel=0))
    return objs

def marker(name, pos, grp="Markers"):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = group(grp)
    e.location = to_b(pos)
    e.empty_display_size = 0.05
    return e

def tape(name, center, length, angle_deg, grp, normal="x", width=0.06, lift=0.006):
    """A strip of duct tape stuck flat on a surface facing +/-x (normal 'x'), +/-z ('z') or up ('y')."""
    if normal == "x":   box(name, center, (lift, width, length), "Tape", grp, rot=(angle_deg, 0, 0), bevel=0.002)
    elif normal == "z": box(name, center, (length, width, lift), "Tape", grp, rot=(0, 0, angle_deg), bevel=0.002)
    else:               box(name, center, (length, lift, width), "Tape", grp, rot=(0, angle_deg, 0), bevel=0.002)

# ============================================================================ the fuselage loft
#
# Cross-section at each z: a rounded box from belly yb to roof yt, half-width w, with fixed "levels" on
# the vertical wall (sill 0.95, window bottom 1.45, door window top 2.1, wall top 2.3) so door and window
# openings have straight edges: every ring has the same vertex layout, and an opening is a rectangle of
# (ring range) x (segment range).

SILL, WIN_LO, WIN_HI, WALL_TOP = 0.95, 1.45, 2.10, 2.30
LO_ARC, UP_ARC = 3, 6       # points on the lower corner arc and the roof arc (per side)

def section(w, yb, yt, inset=0.0, floor=None):
    """Points around the section, starting at the bottom centre going to +x, over the top, down -x. Symmetric."""
    w = w - inset
    yb2 = yb + inset if floor is None else floor
    yt2 = yt - inset * 1.4
    # wall levels squeezed into this ring's height (they only matter where the walls are tall)
    span = (WALL_TOP - SILL)
    def lvl(y):
        if yt2 - yb2 > 1.6: return min(max(y, yb2 + 0.01), yt2 - 0.25)
        k = (y - 0.7) / (2.72 - 0.7)
        return yb2 + k * (yt2 - yb2)
    ys, yw, yh, yT = lvl(SILL), lvl(WIN_LO), lvl(WIN_HI), lvl(WALL_TOP)
    r_lo = min(0.24, (ys - yb2) * 0.95, w * 0.5)
    right = []
    # lower corner arc: from (0, yb) ... to (w, ys)
    right.append(Vector((0, yb2, 0)))
    for i in range(1, LO_ARC + 1):
        a = math.pi / 2 * i / LO_ARC
        right.append(Vector((w - r_lo + math.sin(a) * r_lo, yb2 + r_lo - math.cos(a) * r_lo, 0)))
    right.append(Vector((w, ys, 0)))   # (always the same count of points per ring)
    right.append(Vector((w, yw, 0))); right.append(Vector((w, yh, 0))); right.append(Vector((w, yT, 0)))
    # roof arc: superellipse from (w, yT) to (0, yt)
    h = yt2 - yT
    for i in range(1, UP_ARC + 1):
        t = i / UP_ARC
        a = math.pi / 2 * t
        right.append(Vector((w * math.cos(a) ** 0.85, yT + h * math.sin(a) ** 0.85, 0)))
    right[-1] = Vector((0, yt2, 0))
    left = [Vector((-p.x, p.y, 0)) for p in reversed(right[1:-1])]
    return right + left   # closed ring; index 0 bottom centre, index len(right)-1 top centre

# segment index ranges (right side) for openings: the wall between sill..wall top
IDX_SILL = 1 + LO_ARC          # (w, sill)
IDX_WINLO, IDX_WINHI, IDX_TOP = IDX_SILL + 1, IDX_SILL + 2, IDX_SILL + 3
N_HALF = IDX_TOP + UP_ARC       # index of the top centre
N_RING = 2 * N_HALF

def mirror_idx(i):   # segment/vertex index on the left for the right index i
    return (N_RING - i) % N_RING

# ring z positions and the body profile at each
RINGS = [-1.6, -1.35, -1.1, -0.85, -0.62, -0.5, -0.35, -0.2, 0.2, 0.6, 1.0, 1.2, 1.40, 1.55, 1.9, 2.2, 2.45, 2.55, 2.75, 2.95, 3.08, 3.2, 3.32, 3.4, 3.45]

def profile(z):
    """(half-width, belly y, roof y) of the OUTER skin at z."""
    # width
    if z <= -0.5: w = 0.98 - (-0.5 - z) / 1.1 * 0.62      # rear cone: 0.98 -> 0.36 at z=-1.6
    elif z <= 1.45: w = 0.98
    elif z <= 2.95: w = 0.98                               # (the dash needs the full width)
    else:
        k = min(1.0, (z - 2.95) / 0.5)
        w = 0.06 + 0.92 * math.sqrt(max(0.0, 1 - k * k))     # bulbous nose
    # belly
    if z <= -0.5: yb = 0.70 + (-0.5 - z) / 1.1 * 0.75      # 0.70 -> 1.45 at the tail root
    elif z <= 3.0: yb = 0.70
    else:
        k = min(1.0, (z - 3.0) / 0.45)
        yb = 0.70 + 0.60 * k ** 1.5
    # roof
    if z <= -0.5: yt = 2.72 - (-0.5 - z) / 1.1 * 0.5       # 2.72 -> 2.22
    elif z <= 1.5: yt = 2.72
    elif z <= 1.9: yt = 2.72 - (z - 1.5) / 0.4 * 0.30       # step down to the cockpit roof 2.42
    elif z <= 2.5: yt = 2.42
    else:
        k = (z - 2.5) / 0.92
        yt = 2.42 - 1.10 * (k ** 1.15)                       # the raked windscreen, then the nose
    if z >= 3.45: w, yb, yt = 0.05, 1.31, 1.34
    return w, yb, yt

def region_of(ri, si):
    """What the quad between rings ri,ri+1 and segments si,si+1 belongs to (outer skin)."""
    z0, z1 = RINGS[ri], RINGS[ri + 1]
    zm = (z0 + z1) / 2
    s = si if si < N_HALF else mirror_idx(si + 1)   # mirror the left side onto the right indices
    right = si < N_HALF
    in_wall = IDX_SILL <= s < IDX_TOP                # between sill and wall top
    if in_wall and -0.35 <= z0 and z1 <= 1.40: return "DoorR" if right else "DoorL"
    if IDX_WINLO <= s < IDX_WINHI + 1 and 1.55 <= z0 and z1 <= 2.45: return "SideWin"   # window bottom .. wall top
    if s >= IDX_TOP and 2.55 <= z0 and z1 <= 3.08: return "Windscreen"
    return "Wall"

def door_window(ri, si):
    z0, z1 = RINGS[ri], RINGS[ri + 1]
    s = si if si < N_HALF else mirror_idx(si + 1)
    return IDX_WINLO <= s < IDX_WINHI and -0.2 <= z0 and z1 <= 1.2

class Builder:
    """Collects quads per material into one mesh (flat shaded, each quad its own verts)."""
    def __init__(self): self.faces = {}   # material -> list of (a,b,c,d) in Unity coords
    def quad(self, material, a, b, c, d, facing):
        n = (Vector(b) - Vector(a)).cross(Vector(c) - Vector(a))
        if n.length < 1e-9: return
        if n.dot(Vector(facing)) < 0: b, d = d, b
        self.faces.setdefault(material, []).append((Vector(a), Vector(b), Vector(c), Vector(d)))
    def tri(self, material, a, b, c, facing):
        n = (Vector(b) - Vector(a)).cross(Vector(c) - Vector(a))
        if n.length < 1e-9: return
        if n.dot(Vector(facing)) < 0: b, c = c, b
        self.faces.setdefault(material, []).append((Vector(a), Vector(b), Vector(c)))
    def build(self, name, grp, pivot=(0, 0, 0)):
        me = bpy.data.meshes.new(name)
        bm = bmesh.new()
        mats = list(self.faces.keys())
        for mi, m in enumerate(mats):
            for f in self.faces[m]:
                # (to_b mirrors x, which flips the winding: reverse it so the faces still point 'facing')
                vs = [bm.verts.new(to_b(Vector(p) - Vector(pivot))) for p in reversed(f)]
                try:
                    face = bm.faces.new(vs); face.material_index = mi
                except ValueError:
                    pass
        # Orient the shell consistently and outward (this is robust to the mirrored axes); the
        # lining is the inside surface, so it gets the opposite.
        if name not in ("Jambs", "Glass"):
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            if name == "Lining": bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        for m in mats: me.materials.append(mat(m))
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        o.parent = group(grp)
        return o

def outward(p, z):
    """Roughly outward direction at a skin point (for winding)."""
    v = Vector((p.x, p.y - 1.72, 0))
    return v.normalized() if v.length > 1e-6 else Vector((0, 1, 0))

def build_fuselage():
    outer = [section(*profile(z)) for z in RINGS]
    inner = []
    for z in RINGS:
        w, yb, yt = profile(z)
        inner.append(section(w, yb, yt, inset=0.06, floor=max(yb + 0.06, 0.93) if z > -0.5 else yb + 0.06))
    for ring, z in zip(outer, RINGS):
        for p in ring: p.z = z
    for ring, z in zip(inner, RINGS):
        for p in ring: p.z = z
    hull, lining, jambs, glass = Builder(), Builder(), Builder(), Builder()
    doors = {"DoorL": Builder(), "DoorR": Builder()}
    nr, ns = len(RINGS), N_RING

    def O(r, s): return outer[r][s % ns]
    def I(r, s): return inner[r][s % ns]
    reg = [[region_of(r, s) for s in range(ns)] for r in range(nr - 1)]

    for r in range(nr - 1):
        for s in range(ns):
            a, b, c, d = O(r, s), O(r, s + 1), O(r + 1, s + 1), O(r + 1, s)
            mid = (a + c) / 2
            out = outward(mid, mid.z)
            rg = reg[r][s]
            if rg == "Wall":
                belly = mid.y < 0.78 and mid.z > 1.4
                hull.quad("DarkGrey" if belly else "HullRed", a, b, c, d, out)
                # inner lining only where there's a cabin/cockpit behind it
                if RINGS[r] >= -0.62:
                    ia, ib, ic, id_ = I(r, s), I(r, s + 1), I(r + 1, s + 1), I(r + 1, s)
                    imid = (ia + ic) / 2
                    m = "Grey" if (imid.y > 2.25 and abs(imid.x) < 0.75) else ("PadRed" if imid.z > 1.45 else "PadCream")
                    if imid.y < 0.96 and RINGS[r] >= -0.5: m = "DarkGrey"   # under the floor plate
                    lining.quad(m, ia, ib, ic, id_, -out)
            elif rg in ("DoorL", "DoorR"):
                # the door panel: outer face pushed out 0.035, inner face 0.03 in, on stand-off from the skin
                db = doors[rg]
                oa, ob, oc, od = [p + outward(p, p.z) * 0.035 for p in (a, b, c, d)]
                ia, ib, ic, id_ = [p - outward(p, p.z) * 0.03 for p in (a, b, c, d)]
                if door_window(r, s):
                    db.quad("Glass", *[p + outward(p, p.z) * 0.005 for p in (a, b, c, d)], out)
                else:
                    db.quad("HullRed", oa, ob, oc, od, out)
                    db.quad("PadCream", ia, ib, ic, id_, -out)
            elif rg in ("SideWin", "Windscreen"):
                gm = [(O(r, s) + I(r, s)) / 2 for s in (s, s + 1)] + [(O(r + 1, s + 1) + I(r + 1, s + 1)) / 2, (O(r + 1, s) + I(r + 1, s)) / 2]
                glass.quad("Glass", *gm, out)

    # jambs: every edge between a Wall quad and an opening gets a strip from the inner skin to the outer skin
    def jamb(r, s, nr_, ns_, e0, e1, kind):
        if not (0 <= nr_ < nr - 1): return
        if reg[nr_][ns_ % ns] != "Wall": return
        a, b = I(*e0), I(*e1)
        c, d = O(*e1), O(*e0)
        m = "DarkGrey" if kind in ("SideWin", "Windscreen") else "HullRed"
        # face toward the opening (both sides drawn: seen from inside and outside)
        jambs.quad(m, a, b, c, d, (1, 0, 0)); jambs.quad(m, a, b, c, d, (-1, 0, 0))
    for r in range(nr - 1):
        for s in range(ns):
            rg = reg[r][s]
            if rg == "Wall": continue
            jamb(r, s, r, s + 1, (r, s + 1), (r + 1, s + 1), rg)
            jamb(r, s, r, s - 1, (r, s), (r + 1, s), rg)
            jamb(r, s, r + 1, s, (r + 1, s), (r + 1, s + 1), rg)
            jamb(r, s, r - 1, s, (r, s), (r, s + 1), rg)
    # door edges (the panel's thickness) + sill/rail hardware
    for name, db in doors.items():
        sign = 1 if name == "DoorR" else -1
        for r in range(nr - 1):
            for s in range(ns):
                if reg[r][s] != name: continue
                a, b, c, d = O(r, s), O(r, s + 1), O(r + 1, s + 1), O(r + 1, s)
                for (nr_, ns_, e0, e1) in ((r, s + 1, (r, s + 1), (r + 1, s + 1)), (r, s - 1, (r + 1, s), (r, s)),
                                           (r + 1, s, (r + 1, s + 1), (r + 1, s)), (r - 1, s, (r, s), (r, s + 1))):
                    inside = 0 <= nr_ < nr - 1 and reg[nr_][ns_ % ns] == name
                    if inside: continue
                    p0, p1 = O(*e0), O(*e1)
                    q0, q1 = p0 - outward(p0, p0.z) * 0.03, p1 - outward(p1, p1.z) * 0.03
                    p0, p1 = p0 + outward(p0, p0.z) * 0.035, p1 + outward(p1, p1.z) * 0.035
                    em = (p0 + p1) / 2
                    away = (em - (a + c) / 2); away.z = away.z * 1.0
                    jambs_dir = Vector((away.x, away.y, away.z)).normalized() if away.length > 1e-6 else Vector((0, 1, 0))
                    db.quad("HullRed", p0, p1, q1, q0, jambs_dir)
                # window frame strip on the door around the glass
                if door_window(r, s):
                    for (nr_, ns_, e0, e1) in ((r, s + 1, (r, s + 1), (r + 1, s + 1)), (r, s - 1, (r + 1, s), (r, s)),
                                               (r + 1, s, (r + 1, s + 1), (r + 1, s)), (r - 1, s, (r, s), (r, s + 1))):
                        if 0 <= nr_ < nr - 1 and reg[nr_][ns_ % ns] == name and door_window(nr_, ns_ % ns): continue
                        p0, p1 = O(*e0), O(*e1)
                        db.quad("DarkGrey", p0 + outward(p0, p0.z) * 0.035, p1 + outward(p1, p1.z) * 0.035,
                                p1 - outward(p1, p1.z) * 0.03, p0 - outward(p0, p0.z) * 0.03, (0, 0, 1))
                        db.quad("DarkGrey", p0 + outward(p0, p0.z) * 0.035, p1 + outward(p1, p1.z) * 0.035,
                                p1 - outward(p1, p1.z) * 0.03, p0 - outward(p0, p0.z) * 0.03, (0, 0, -1))
    # end caps: rear of the cone (closed by the tail root) and the nose tip
    tip = outer[-1]
    ctr = sum(tip, Vector()) / len(tip)
    for s in range(ns): hull.tri("HullRed", ctr, tip[s], tip[(s + 1) % ns], (0, 0, 1))
    back = outer[0]
    ctr = sum(back, Vector()) / len(back)
    for s in range(ns): hull.tri("HullRed", ctr, back[s], back[(s + 1) % ns], (0, 0, -1))
    # lining ends: rear wall of the cabin (at z=-0.5 ring) is the interior's padded wall; close the lining
    # at its rearmost ring with a cap so nothing looks through into the tail
    rw = [p for p in inner[RINGS.index(-0.62)]]
    ctr = sum(rw, Vector()) / len(rw)
    for s in range(ns): lining.tri("PadCream", ctr, rw[s], rw[(s + 1) % ns], (0, 0, 1))
    nose = [p for p in inner[RINGS.index(3.32)]]
    ctr = sum(nose, Vector()) / len(nose)
    for s in range(ns): lining.tri("PadRed", ctr, nose[s], nose[(s + 1) % ns], (0, 0, -1))

    hull.build("Hull", "Body")
    lining.build("Lining", "Body")
    jambs.build("Jambs", "Body")
    glass.build("Glass", "Body")
    for name, db in doors.items():
        db.build(name.replace("Door", "Door_"), name.replace("Door", "Door_"))
    return outer

# ============================================================================ everything else

def build_rest(outer):
    # ---------------- door rails: an upper track on stand-offs, a lower track at the sill, roller carriages on the doors
    for sign, side in ((1, "R"), (-1, "L")):
        grp = "Rails"
        x_wall = 0.98 * sign
        # upper rail runs from the door's front post back over the rear cone
        rail_y = WALL_TOP + 0.06
        pts = [(x_wall + sign * 0.06, rail_y, 1.45), (x_wall + sign * 0.06, rail_y, -0.5), (0.66 * sign + sign * 0.06, rail_y - 0.02, -1.25)]
        tube(f"RailTop_{side}", pts, 0.022, "Metal", grp, segs=8)
        for z in (1.35, 0.5, -0.35, -1.0):
            w = profile(z)[0]
            box(f"RailBracket_{side}_{z}", ((w + 0.02) * sign, rail_y, z), (0.08, 0.05, 0.06), "DarkGrey", grp, bevel=0.006)
        # lower rail along the sill
        tube(f"RailLow_{side}", [(x_wall + sign * 0.05, SILL - 0.05, 1.45), (x_wall + sign * 0.05, SILL - 0.05, -0.5), (0.9 * sign + sign * 0.05, SILL - 0.03, -0.68)], 0.018, "Metal", grp, segs=8)
        # carriages (on the door, so they slide with it)
        dg = f"Door_{side}"
        for z in (-0.2, 1.2):
            box(f"Carriage_{side}_{z}", ((x_wall + sign * 0.045), rail_y, z), (0.06, 0.09, 0.12), "DarkGrey", dg, bevel=0.008)
            box(f"CarriageArm_{side}_{z}", ((x_wall + sign * 0.035), rail_y - 0.06, z), (0.03, 0.08, 0.06), "DarkGrey", dg, bevel=0.004)
        # door handle (outside) and a grab bar (inside)
        box(f"DoorHandle_{side}", ((x_wall + sign * 0.06), 1.62, 1.28), (0.03, 0.05, 0.18), "DarkGrey", dg, bevel=0.008)
        tube(f"DoorGrab_{side}", [((x_wall - sign * 0.04), 1.5, 1.2), ((x_wall - sign * 0.09), 1.5, 1.2), ((x_wall - sign * 0.09), 1.95, 1.2), ((x_wall - sign * 0.04), 1.95, 1.2)], 0.014, "Yellow", dg, segs=6)
        # sill step below the door
        box(f"Step_{side}", ((x_wall + sign * 0.16), SILL - 0.18, 0.55), (0.28, 0.035, 1.5), "DarkGrey", "Details", bevel=0.006)
        for z in (-0.05, 1.15):
            tube(f"StepBrace_{side}_{z}", [((x_wall - sign * 0.02), SILL - 0.3, z), ((x_wall + sign * 0.26), SILL - 0.19, z)], 0.014, "Metal", "Details", segs=5)

    # ---------------- windscreen frame: centre post + a header bar, wiper
    box("WindscreenPost", (0, 2.05, 2.86), (0.05, 0.9, 0.06), "DarkGrey", "Details", rot=(-52, 0, 0), bevel=0.006)
    tube("Wiper", [(0.35, 1.72, 3.03), (0.05, 2.02, 2.83)], 0.012, "Black", "Details", segs=5)
    box("WiperBlade", (0.2, 1.87, 2.93), (0.02, 0.5, 0.02), "Rubber", "Details", rot=(-52, 0, 15), bevel=0.003)

    # ---------------- nose: landing light + pitot
    cyl("NoseLampHousing", (0, 0.78, 3.28), 0.11, 0.14, "DarkGrey", "Details", rot=(90, 0, 0), segs=12)
    cyl("NoseLampLens", (0, 0.78, 3.36), 0.085, 0.02, "Yellow", "Details", rot=(90, 0, 0), segs=12)

    # ---------------- engine deck: cowling on the roof, intake, two exhausts, the rotor mast fairing
    box("Cowling", (0, 2.86, 0.55), (1.2, 0.3, 1.9), "HullRed", "Engine", bevel=0.06)
    box("CowlingTop", (0, 3.0, 0.45), (0.9, 0.12, 1.5), "HullRed", "Engine", bevel=0.05)
    box("Intake", (0, 2.9, 1.52), (0.7, 0.24, 0.14), "DarkGrey", "Engine", bevel=0.02)
    for i in range(5):
        box(f"IntakeSlat_{i}", (-0.24 + i * 0.12, 2.9, 1.6), (0.03, 0.2, 0.02), "Black", "Engine", bevel=0.003)
    for sx in (-0.3, 0.3):
        cyl(f"Exhaust_{sx}", (sx, 2.9, -0.45), 0.09, 0.35, "DarkGrey", "Engine", rot=(75, 0, 0), segs=10)
        cyl(f"ExhaustLip_{sx}", (sx, 2.87, -0.6), 0.1, 0.03, "Black", "Engine", rot=(75, 0, 0), segs=10)
    box("MastFairing", (0, 3.1, 0.55), (0.5, 0.16, 0.6), "HullRed", "Engine", bevel=0.05)
    for k in range(4):   # cowling latches
        box(f"Latch_{k}", (0.61 if k % 2 else -0.61, 2.82, -0.2 + (k // 2) * 1.3), (0.02, 0.08, 0.1), "Metal", "Engine", bevel=0.004)
    # antennas
    tube("AntennaA", [(0.3, 3.05, -0.2), (0.3, 3.35, -0.25)], 0.008, "Black", "Details", segs=4)
    box("AntennaBlade", (-0.25, 0.62, 0.3), (0.02, 0.14, 0.2), "Black", "Details", rot=(0, 0, 0), bevel=0.004)

    # ---------------- main rotor (pivot at the hub centre; spins about Y)
    HUB = (0, 3.3, 0.55)
    cyl("Mast", (0, 3.2, 0.55), 0.07, 0.24, "DarkGrey", "MainRotor", segs=10, pivot=HUB)
    cyl("Hub", HUB, 0.2, 0.14, "DarkGrey", "MainRotor", segs=12, pivot=HUB)
    cyl("HubCap", (0, 3.4, 0.55), 0.09, 0.08, "Metal", "MainRotor", segs=10, pivot=HUB)
    for k in range(4):
        ang = k * 90
        box(f"BladeRoot_{k}", (0, 3.3, 0.55), (0.1, 0.07, 0.56), "Metal", "MainRotor", rot=(0, ang, 0), bevel=0.01, pivot=HUB)
        # blade: a long thin box from the hub outwards, drooping a touch, with a black tip and a yellow stripe
        bm = bmesh.new()
        L = 5.1
        pts = [Vector((-0.13, -0.02, 0.25)), Vector((0.13, -0.02, 0.25)), Vector((0.13, 0.03, 0.25)), Vector((-0.13, 0.03, 0.25)),
               Vector((-0.11, -0.12, L)), Vector((0.11, -0.12, L)), Vector((0.11, -0.08, L)), Vector((-0.11, -0.08, L))]
        vs = [bm.verts.new(p) for p in pts]
        for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            bm.faces.new([vs[i] for i in f])
        m = Matrix.Translation(Vector(HUB)) @ unity_rot(0, ang, 0)
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        finish(bm, f"Blade_{k}", "Black", "MainRotor", pivot=HUB, bevel=0)
        d = unity_rot(0, ang, 0) @ Vector((0, -0.085, 4.3, 1))
        box(f"BladeStripe_{k}", (HUB[0] + d.x, HUB[1] + d.y, HUB[2] + d.z), (0.24, 0.05, 0.3), "Yellow", "MainRotor", rot=(0, ang, 0), bevel=0, pivot=HUB)
    group("MainRotor").location = to_b(HUB)
    marker("M_RotorHub", HUB)

    # ---------------- tail boom (a tapered octagon), fin, stabiliser, tail rotor
    boom = Builder()
    BZ = [-1.3, -2.0, -2.7, -3.4, -4.1, -4.7]
    def boom_ring(z):
        k = (z + 1.3) / -3.4
        r = 0.4 - 0.19 * k
        cy = 1.85 + 0.2 * k
        return [Vector((math.sin(a) * r * 0.9, cy + math.cos(a) * r, z)) for a in [(i + 0.5) * math.pi / 4 for i in range(8)]]
    rings = [boom_ring(z) for z in BZ]
    for i in range(len(rings) - 1):
        for s in range(8):
            a, b, c, d = rings[i][s], rings[i][(s + 1) % 8], rings[i + 1][(s + 1) % 8], rings[i + 1][s]
            mid = (a + c) / 2
            boom.quad("HullRed", a, b, c, d, Vector((mid.x, mid.y - (1.85 + 0.2 * ((mid.z + 1.3) / -3.4)), 0)))
    end = rings[-1]; ctr = sum(end, Vector()) / 8
    for s in range(8): boom.tri("DarkGrey", ctr, end[s], end[(s + 1) % 8], (0, 0, -1))
    boom.build("Boom", "Tail")
    # tail fin: swept vertical fin
    fin = Builder()
    fin_pts = [(-4.25, 2.05), (-4.85, 2.05), (-5.05, 3.15), (-4.75, 3.25)]   # (z, y) outline
    for sx, facing in ((0.05, (1, 0, 0)), (-0.05, (-1, 0, 0))):
        a, b, c, d = [Vector((sx, y, z)) for (z, y) in fin_pts]
        fin.quad("HullRed", a, b, c, d, facing)
    for i in range(4):   # edges
        (z0, y0), (z1, y1) = fin_pts[i], fin_pts[(i + 1) % 4]
        a, b = Vector((0.05, y0, z0)), Vector((0.05, y1, z1)); c, d = Vector((-0.05, y1, z1)), Vector((-0.05, y0, z0))
        out = Vector((0, (y0 + y1) / 2 - 2.6, (z0 + z1) / 2 + 4.6))
        fin.quad("HullRed", a, b, c, d, out)
    fin.build("Fin", "Tail")
    # horizontal stabiliser with end plates
    box("Stabiliser", (0, 2.35, -3.85), (2.0, 0.06, 0.42), "HullRed", "Tail", bevel=0.01)
    for sx in (-1, 1):
        box(f"EndPlate_{sx}", (sx * 1.0, 2.45, -3.85), (0.05, 0.36, 0.44), "HullRed", "Tail", bevel=0.008)
    # tail skid
    tube("TailSkid", [(0, 1.8, -4.3), (0, 1.5, -4.55), (0, 1.52, -4.75)], 0.016, "Metal", "Tail", segs=6)
    # tail rotor: pivot on the left of the fin, spins about X (2 blades) with a small guard ring
    TH = (-0.22, 2.55, -4.72)
    cyl("TailHub", TH, 0.07, 0.12, "DarkGrey", "TailRotor", rot=(0, 0, 90), segs=8, pivot=TH)
    cyl("TailHubCap", (-0.31, 2.55, -4.72), 0.04, 0.05, "Metal", "TailRotor", rot=(0, 0, 90), segs=8, pivot=TH)
    for k in range(2):
        ang = k * 90 + 20
        box(f"TailBlade_{k}", TH, (0.04, 1.7, 0.15), "Black", "TailRotor", rot=(ang, 0, 0), bevel=0, pivot=TH)
        for e in (-1, 1):   # yellow tips
            d = unity_rot(ang, 0, 0) @ Vector((0, e * 0.8, 0, 1))
            box(f"TailBladeTip_{k}_{e}", (TH[0], TH[1] + d.y, TH[2] + d.z), (0.042, 0.14, 0.152), "Yellow", "TailRotor", rot=(ang, 0, 0), bevel=0, pivot=TH)
    group("TailRotor").location = to_b(TH)
    marker("M_TailHub", TH)
    cyl("TailGearbox", (-0.12, 2.55, -4.72), 0.11, 0.12, "DarkGrey", "Tail", rot=(0, 0, 90), segs=10)

    # ---------------- skids: two tubes with turned-up fronts, on arched cross struts
    for sx in (-1, 1):
        x = 0.98 * sign_(sx)
        grp = "Skids"
        tube(f"Skid_{sx}", [(x, 0.08, -1.0), (x, 0.08, 2.5), (x, 0.28, 2.95)], 0.05, "DarkGrey", grp, segs=8)
        cyl(f"SkidCap_{sx}", (x, 0.08, -1.0), 0.05, 0.03, "Black", grp, rot=(90, 0, 0), segs=8)
        for z in (0.15, 1.95):
            # arched strut from the belly down and out to the skid, with a saddle clamp on the skid
            tube(f"Strut_{sx}_{z}", [(0.55 * sx, 0.74, z), (0.86 * sx, 0.5, z), (x, 0.2, z)], 0.045, "DarkGrey", grp, segs=8)
            box(f"Saddle_{sx}_{z}", (x, 0.1, z), (0.16, 0.12, 0.12), "Black", grp, bevel=0.01)
            box(f"StrutRoot_{sx}_{z}", (0.52 * sx, 0.7, z), (0.2, 0.06, 0.16), "DarkGrey", "Details", bevel=0.008)
    # ---------------- tape patches (the goofy signature)
    tape("TapeSide1", (0.99, 2.2, 0.0), 0.3, 60, "Details", normal="x")
    tape("TapeRoofl", (0.2, 2.73, -0.1), 0.3, 40, "Details", normal="y")
    tape("TapeBelly", (0.3, 0.69, 1.6), 0.36, 70, "Details", normal="y")
    # a red cross on the doors' lower panels and the belly rescue markings are left to the door texture (none): simple cross
    build_livery()

def sign_(s): return 1 if s > 0 else -1

# ============================================================================ livery + wear
FONT = {  # 5 wide x 7 tall, top row first
    "R": ["1111.", "1...1", "1...1", "1111.", "1.1..", "1..1.", "1...1"],
    "E": ["11111", "1....", "1....", "1111.", "1....", "1....", "11111"],
    "S": [".1111", "1....", "1....", ".111.", "....1", "....1", "1111."],
    "C": [".1111", "1....", "1....", "1....", "1....", "1....", ".1111"],
    "U": ["1...1", "1...1", "1...1", "1...1", "1...1", "1...1", ".111."],
    "A": [".111.", "1...1", "1...1", "11111", "1...1", "1...1", "1...1"],
    "1": ["..1..", ".11..", "..1..", "..1..", "..1..", "..1..", ".111."],
    "7": ["11111", "....1", "...1.", "..1..", ".1...", ".1...", ".1..."],
    "-": [".....", ".....", ".....", "11111", ".....", ".....", "....."],
    " ": [".....", ".....", ".....", ".....", ".....", ".....", "....."],
}

def side_text(text, sign, y_centre, z_centre, height, material, grp, x_of=None, x=None, thick=0.012, name="Txt"):
    """Block letters on a surface facing +/-x. Reads left-to-right for someone standing on that side:
    on the right side the text runs nose->tail (-z), on the left side tail->nose (+z).
    x_of(z) gives the surface's x at z (or a fixed x)."""
    px = height / 7.0
    # Unity is left-handed: from the right side (+x) the nose (+z) is on the viewer's RIGHT, so text runs +z there
    dz = 1 if sign > 0 else -1
    z = z_centre - dz * (len(text) * 6 - 1) * px / 2
    for ci, ch in enumerate(text):
        rows = FONT.get(ch, FONT[" "])
        for r, row in enumerate(rows):
            # merge runs of pixels in a row into one box
            c = 0
            while c < 5:
                if row[c] == "1":
                    c0 = c
                    while c < 5 and row[c] == "1": c += 1
                    n = c - c0
                    zc = z + dz * (c0 + n / 2.0) * px
                    yc = y_centre + (3 - r) * px
                    xs = (x_of(zc) if x_of else x)
                    box(f"{name}_{ci}_{r}_{c0}", (sign * (xs + thick / 2), yc, zc), (thick, px * 0.92, n * px * 0.95), material, grp, bevel=0)
                else:
                    c += 1
        z += dz * (6 * px)

def side_strip(y0, y1, z0, z1, sign, material, grp, name, lift=0.006, thick=0.012):
    """A painted band on the fuselage wall between z0 and z1, following the hull's width per ring interval."""
    zs = [z for z in RINGS if z0 < z < z1]
    zs = [z0] + zs + [z1]
    for i in range(len(zs) - 1):
        a, b = zs[i], zs[i + 1]
        wa, wb = profile(a)[0], profile(b)[0]
        L = math.hypot(b - a, wb - wa)
        yaw = math.degrees(math.atan2(wb - wa, b - a)) * sign
        cx = (wa + wb) / 2 + lift
        box(f"{name}_{i}", (sign * cx, (y0 + y1) / 2, (a + b) / 2), (thick, y1 - y0, L), material, grp, rot=(0, -yaw if sign > 0 else yaw, 0), bevel=0)

def build_livery():
    grp = "Details"
    def boom_r(z): return 0.4 - 0.19 * ((z + 1.3) / -3.4)
    def boom_cy(z): return 1.85 + 0.2 * ((z + 1.3) / -3.4)
    def boom_side_x(z): return boom_r(z) * 0.9 * math.cos(math.radians(22.5))
    for sign in (1, -1):
        # white band under the windows along the whole fuselage: on the hull fore and aft of the door...
        side_strip(1.24, 1.40, 1.42, 2.93, sign, "White", grp, f"Band_fwd_{sign}")
        side_strip(1.24, 1.40, -1.0, -0.37, sign, "White", grp, f"Band_aft_{sign}")
        # ...and on the door itself (built into the door mesh so it slides with it)
        dg = "Door_R" if sign > 0 else "Door_L"
        box(f"Band_door_{sign}", (sign * 1.021, 1.32, 0.525), (0.012, 0.16, 1.72), "White", dg, bevel=0)
        # RESCUE on the door's lower panel
        side_text("RESCUE", sign, 1.09, 0.52, 0.17, "White", dg, x=1.015, name=f"DoorTxt_{sign}")
        # RESCUE along the tail boom, chevrons near the tail, registration on the fin
        side_text("RESCUE", sign, boom_cy(-2.6) + 0.02, -2.5, 0.15, "White", grp, x_of=boom_side_x, name=f"BoomTxt_{sign}")
        for k in range(5):
            z = -3.45 - k * 0.14
            box(f"Chevron_{sign}_{k}", (sign * (boom_side_x(z) + 0.006), boom_cy(z), z), (0.012, 0.32, 0.07), "Yellow" if k % 2 == 0 else "Black", grp, rot=(40, 0, 0), bevel=0)
        side_text("R-17", sign, 2.62, -4.62, 0.14, "Black", grp, x=0.05, name=f"Reg_{sign}")
        # small cross at the front of the cabin, above the band
        box(f"CrossH_{sign}", (sign * 1.0, 1.72, 1.55), (0.012, 0.05, 0.16), "White", grp, bevel=0)
        box(f"CrossV_{sign}", (sign * 1.0, 1.72, 1.55), (0.012, 0.16, 0.05), "White", grp, bevel=0)
    # yellow/black rotor-warning stripes on the tail fin leading edge and a big white number on the belly
    for k in range(4):
        box(f"FinStripe_{k}", (0, 2.2 + k * 0.22, -4.32 - k * 0.04), (0.112, 0.11, 0.1), "Yellow" if k % 2 == 0 else "Black", grp, rot=(-18, 0, 0), bevel=0)

    # ---------------- wear and tear: this thing has been through it
    # replacement panels that never got painted: a primer-grey one on the rear cone, a darker red one on the boom
    box("PrimerPanel", (-0.83, 1.9, -0.72), (0.014, 0.42, 0.36), "Primer", grp, rot=(0, 18, 0), bevel=0)
    box("PrimerPanelRivets", (-0.845, 1.9, -0.72), (0.014, 0.44, 0.03), "DarkGrey", grp, rot=(0, 18, 0), bevel=0)
    box("DarkPanel", (boom_side_x(-2.0) + 0.006, boom_cy(-2.0) - 0.02, -1.95), (0.012, 0.22, 0.3), "HullRedDark", grp, bevel=0)
    # duct tape: windscreen corner crack, a door window corner, wraps around the boom and a skid strut, the cowling
    for (n, c, l, a) in (("WsTape1", (-0.72, 2.22, 2.66), 0.3, 35), ("WsTape2", (-0.7, 2.24, 2.65), 0.26, -40)):   # top corner, co-pilot side: out of the pilot's eyeline
        box(n, c, (l, 0.055, 0.008), "Tape", grp, rot=(-49, 0, a), bevel=0.002)
    tape("DoorWinTape", (1.03, 1.55, -0.1), 0.24, 50, "Door_R", normal="x")
    tape("DoorWinTape2", (1.03, 1.52, -0.13), 0.2, -35, "Door_R", normal="x")
    for z in (-2.25, -3.05):
        cyl(f"BoomWrap_{z}", (0, boom_cy(z), z), boom_r(z) * 0.98 + 0.01, 0.07, "Tape", grp, rot=(90, 0, 0), segs=8, bevel=0)
    cyl("StrutWrap", (0.78, 0.55, 1.95), 0.056, 0.09, "Tape", grp, rot=(0, 0, 52), segs=8, bevel=0)
    tape("CowlTape1", (0.3, 3.01 + 0.06, 0.2), 0.36, 30, grp, normal="y")
    tape("CowlTape2", (0.26, 3.01 + 0.06, 0.24), 0.28, -50, grp, normal="y")
    tape("NoseTape", (-0.7, 1.1, 3.05), 0.3, 60, grp, normal="x")
    tape("FinTape", (0.06, 3.0, -4.75), 0.26, 65, grp, normal="x")
    # soot streaks behind the exhausts, a dangling wire under the belly, a bent antenna, one mismatched blade tip
    for sx in (-0.3, 0.3):
        box(f"Soot_{sx}", (sx, 2.77, -0.95), (0.16, 0.012, 0.5), "Soot", grp, rot=(0, 0, 0), bevel=0)
    tube("LooseWire", [(0.2, 0.7, 0.9), (0.24, 0.5, 0.8), (0.3, 0.56, 0.6), (0.34, 0.7, 0.5)], 0.008, "Black", grp, segs=4)
    tube("BentAntenna", [(-0.3, 3.05, -0.1), (-0.3, 3.25, -0.14), (-0.42, 3.31, -0.22)], 0.008, "Black", grp, segs=4)
    # one blade tip is a different colour (a replacement)
    o = bpy.data.objects.get("BladeStripe_1")
    if o: o.data.materials[0] = mat("White")

def merge_group(name, pivot_unity):
    """Join every mesh under the group empty into one mesh object named after it, origin at the pivot."""
    g = group(name)
    origin = to_b(pivot_unity) if pivot_unity is not None else g.location.copy()
    bpy.context.view_layer.update()   # (matrix_world of freshly parented objects is stale until this)
    bm = bmesh.new()
    mats = []
    kids = [o for o in bpy.data.objects if o.parent == g and o.type == 'MESH']
    for o in kids:
        me = o.data
        mw = o.matrix_world
        base = {}
        for mi, m in enumerate(me.materials):
            if m not in mats: mats.append(m)
            base[mi] = mats.index(m)
        vs = [bm.verts.new(mw @ v.co - origin) for v in me.vertices]
        for p in me.polygons:
            try:
                f = bm.faces.new([vs[i] for i in p.vertices])
                f.material_index = base.get(p.material_index, 0)
                f.smooth = False
            except ValueError:
                pass
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = False
    for o in kids: bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(g, do_unlink=True)
    del GROUPS[name]
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = group("Body")
    o.location = origin
    return o

def build():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    GROUPS.clear()
    root = group("Body")
    # Every part sits directly under the root: Blender's exporter adds a 90-degree twist per level of
    # nested empties, so nesting depth must be the same for everything (the moving groups are one
    # level deeper for their children, which Unity re-parents anyway).
    for g in ("Door_L", "Door_R", "MainRotor", "TailRotor"):
        group(g, "Body")
    for g in ("Rails", "Details", "Engine", "Tail", "Skids", "Markers"):
        GROUPS[g] = root
    outer = build_fuselage()
    build_rest(outer)
    # Moving parts become ONE mesh each, directly under the root, with the origin at the pivot (doors:
    # their closed position; rotors: the hub). Nested empties would import with a twist.
    merge_group("Door_L", (-0.98, 1.6, 0.52))
    merge_group("Door_R", (0.98, 1.6, 0.52))
    merge_group("MainRotor", None)
    merge_group("TailRotor", None)
    for o in bpy.data.objects: o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE', use_custom_props=False)
    return len([o for o in bpy.data.objects if o.type == 'MESH'])

count = build()
print(f"[build_body] exported {count} meshes -> {OUT}")
