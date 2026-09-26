"""
Builds the medevac helicopter interior (cockpit + cabin) and exports it to Unity.
Run inside Blender:  exec(open("/Users/samwudel/Game/tools/blender/build_interior.py").read())

Everything is authored in UNITY helicopter-local coordinates (x right, y up, z forward, metres;
floor at y=0.95, cabin z -0.5..1.45, cockpit z 1.45..3.3) and converted to Blender on creation.
Target look: ArtSource/Concept/concept_cockpit.png and concept_cabin.png.

Output: RescueGame/Assets/Art/Helicopters/Medevac/Interior/Interior.fbx
  - static pieces grouped under Shell / Cockpit / Cabin / Props
  - moving parts with pivots at their hinge: Cyclic, Collective, PedalL, PedalR
  - empties (markers) Unity uses to place gauges, lights, notes, grips and seats: M_*
Materials are named Mat_<Name>; Unity maps them to RescueGame/Stylized Lit colours.
"""
import bpy, bmesh, math, os
from mathutils import Matrix, Vector, Euler

OUT = "/Users/samwudel/Game/RescueGame/Assets/Art/Helicopters/Medevac/Interior/Interior.fbx"

PALETTE = {
    "HullRed": (0.74, 0.17, 0.11), "PadRed": (0.70, 0.24, 0.18), "PadCream": (0.86, 0.80, 0.66),
    "Grey": (0.45, 0.45, 0.47), "DarkGrey": (0.17, 0.17, 0.19), "Black": (0.06, 0.06, 0.07),
    "Metal": (0.62, 0.63, 0.66), "Floor": (0.30, 0.29, 0.28), "Tape": (0.68, 0.68, 0.64),
    "Yellow": (0.96, 0.74, 0.12), "Orange": (0.98, 0.47, 0.10), "Seat": (0.25, 0.26, 0.29),
    "Cushion": (0.66, 0.20, 0.16), "Olive": (0.36, 0.42, 0.30), "White": (0.92, 0.92, 0.90),
    "FirstAid": (0.82, 0.12, 0.10), "Brass": (0.72, 0.58, 0.30), "Net": (0.83, 0.76, 0.58),
    "Bag": (0.33, 0.40, 0.27), "Rubber": (0.12, 0.12, 0.12), "RedKnob": (0.85, 0.10, 0.08),
    "PanelFace": (0.23, 0.23, 0.25),
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
    # Unity order: Z, then X, then Y. Standard rotation maths on Unity coordinates gives exactly
    # Unity's visual result (+rx tips the top forward, +ry turns forward to the right, +rz turns
    # right toward up), so no sign changes are needed.
    return (Matrix.Rotation(math.radians(ry), 4, 'Y') @ Matrix.Rotation(math.radians(rx), 4, 'X')
            @ Matrix.Rotation(math.radians(rz), 4, 'Z'))

def finish(bm, name, material, grp, pivot=(0, 0, 0), bevel=0.012):
    if bevel > 0:
        edges = [e for e in bm.edges]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=1, affect='EDGES', clamp_overlap=True)
    # convert Unity-space verts to Blender space relative to pivot
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
    # verts are relative to the pivot; moving-part groups are themselves placed at their pivot
    return o

def box(name, center, size, material, grp, rot=(0, 0, 0), bevel=0.012, pivot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Translation(Vector(center)) @ unity_rot(*rot) @ Matrix.Diagonal(Vector((*size, 1)))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return finish(bm, name, material, grp, pivot, min(bevel, min(size) * 0.3))

def cyl(name, center, radius, length, material, grp, rot=(0, 0, 0), segs=10, bevel=0.006, pivot=(0, 0, 0)):
    """Cylinder along its local Y axis (Unity up) before rotation."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=length)
    # create_cone is along Z; turn to Y
    bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(-90), 4, 'X'), verts=bm.verts)
    m = Matrix.Translation(Vector(center)) @ unity_rot(*rot)
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return finish(bm, name, material, grp, pivot, min(bevel, radius * 0.3))

def tube(name, pts, radius, material, grp, segs=6):
    """A bent tube (frames, rails, handles) through a list of Unity points."""
    objs = []
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        d = b - a
        L = d.length
        if L < 1e-4: continue
        # orient Y axis along d
        up = Vector((0, 1, 0))
        q = up.rotation_difference(d.normalized())
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=L + radius * 1.2)
        bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(-90), 4, 'X'), verts=bm.verts)
        bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4(), verts=bm.verts)
        objs.append(finish(bm, f"{name}_{i}", material, grp, bevel=0))
    return objs

def marker(name, pos, grp="Markers", rot=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = group(grp)
    e.location = to_b(pos)
    e.empty_display_size = 0.05
    return e

def pads(prefix, x0, x1, y0, y1, z, material, grp, cell=0.26, depth=0.05, normal_z=1, gap=0.018):
    """Quilted padding: a grid of bevelled cushions on a wall facing +/-z."""
    nx = max(1, round((x1 - x0) / cell)); ny = max(1, round((y1 - y0) / cell))
    w = (x1 - x0) / nx; h = (y1 - y0) / ny
    box(prefix + "_back", ((x0 + x1) / 2, (y0 + y1) / 2, z - normal_z * 0.01), (x1 - x0, y1 - y0, 0.02), "DarkGrey", grp, bevel=0)
    for i in range(nx):
        for j in range(ny):
            box(f"{prefix}_{i}_{j}", (x0 + w * (i + 0.5), y0 + h * (j + 0.5), z + normal_z * depth / 2),
                (w - gap, h - gap, depth), material, grp, bevel=0.018)

def pads_x(prefix, z0, z1, y0, y1, x, material, grp, cell=0.26, depth=0.05, normal_x=1, gap=0.018):
    """Quilted padding on a wall facing +/-x."""
    nz = max(1, round((z1 - z0) / cell)); ny = max(1, round((y1 - y0) / cell))
    w = (z1 - z0) / nz; h = (y1 - y0) / ny
    box(prefix + "_back", (x - normal_x * 0.01, (y0 + y1) / 2, (z0 + z1) / 2), (0.02, y1 - y0, z1 - z0), "DarkGrey", grp, bevel=0)
    for i in range(nz):
        for j in range(ny):
            box(f"{prefix}_{i}_{j}", (x + normal_x * depth / 2, y0 + h * (j + 0.5), z0 + w * (i + 0.5)),
                (depth, h - gap, w - gap), material, grp, bevel=0.018)

def tape(name, center, length, angle_deg, grp, normal="z", width=0.05):
    if normal == "z":
        box(name, center, (length, width, 0.006), "Tape", grp, rot=(0, 0, angle_deg), bevel=0.002)
    else:
        box(name, center, (0.006, width, length), "Tape", grp, rot=(angle_deg, 0, 0), bevel=0.002)

# ============================================================================ build

def build():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    GROUPS.clear()
    root = group("Interior")
    for g in ("Shell", "Cockpit", "Cabin", "Props", "Markers"): group(g, "Interior")

    F = 0.95  # floor height
    # ---------------------------------------------------------------- floor
    box("Floor", (0, F - 0.02, 1.35), (1.84, 0.04, 3.75), "Floor", "Shell", bevel=0)
    for i in range(14):  # anti-slip metal strips
        z = -0.4 + i * 0.26
        if z > 2.0: break
        box(f"FloorStrip_{i}", (0, F + 0.004, z), (1.6, 0.008, 0.035), "Metal", "Shell", bevel=0.002)
    box("DoorSill_L", (-0.9, F + 0.01, 0.52), (0.1, 0.02, 1.7), "Yellow", "Shell", bevel=0.004)
    box("DoorSill_R", (0.9, F + 0.01, 0.52), (0.1, 0.02, 1.7), "Yellow", "Shell", bevel=0.004)

    # ---------------------------------------------------------------- cabin shell
    ROOF = 2.62
    pads("RearWall", -0.9, 0.9, F, 2.3, -0.52, "PadCream", "Cabin", normal_z=1)
    box("RearWallTrim", (0, 2.31, -0.5), (1.8, 0.05, 0.05), "HullRed", "Cabin")
    # sloped upper walls above the doors (inward to the roof)
    # grab handles on the door posts (the ones the crew grab)
    for s in (-1, 1):
        for z in (-0.3, 1.34):
            tube(f"PostHandle_{s}_{z}", [(s * 0.88, 1.5, z), (s * 0.84, 1.52, z), (s * 0.84, 1.98, z), (s * 0.88, 2.0, z)], 0.016, "Yellow", "Cabin")

    # ---------------------------------------------------------------- bulkhead (cabin side of cockpit)
    for s, n in ((-1, "L"), (1, "R")):
        box(f"Bulkhead_{n}", (s * 0.655, 1.77, 1.46), (0.55, 1.64, 0.05), "PadCream", "Cabin")
        box(f"BulkheadTrim_{n}", (s * 0.385, 1.77, 1.46), (0.05, 1.64, 0.07), "HullRed", "Cabin")
    box("BulkheadHeader", (0, 2.52, 1.46), (0.72, 0.2, 0.05), "PadCream", "Cabin")
    tape("BulkTape1", (-0.62, 2.2, 1.43), 0.3, 25, "Cabin")
    tape("BulkTape2", (-0.6, 2.2, 1.43), 0.2, -35, "Cabin")
    tape("BulkTape3", (0.55, 1.2, 1.43), 0.22, 10, "Cabin")

    # ---------------------------------------------------------------- rear bench with straps
    for s in (-1, 1):
        tube(f"BenchFrame_{s}", [(s * 0.78, F, -0.1), (s * 0.78, 1.3, -0.1), (s * 0.78, 1.3, -0.46)], 0.022, "Metal", "Cabin")
    tube("BenchFrameFront", [(-0.8, 1.3, -0.08), (0.8, 1.3, -0.08)], 0.022, "Metal", "Cabin")
    for k, x in enumerate((-0.55, 0.0, 0.55)):
        box(f"BenchSeat_{k}", (x, 1.36, -0.27), (0.5, 0.1, 0.4), "Cushion", "Cabin", bevel=0.03)
        box(f"BenchBack_{k}", (x, 1.72, -0.45), (0.5, 0.6, 0.08), "Cushion", "Cabin", rot=(-6, 0, 0), bevel=0.03)
        for sx in (-0.13, 0.13):  # shoulder straps
            box(f"Strap_{k}_{sx}", (x + sx, 1.62, -0.4), (0.05, 0.62, 0.012), "Orange", "Cabin", rot=(-6, 0, 0), bevel=0.003)
        box(f"LapStrap_{k}", (x, 1.42, -0.1), (0.46, 0.05, 0.012), "Orange", "Cabin", rot=(70, 0, 0), bevel=0.003)
        marker(f"M_BenchSeat_{k}", (x, 1.41, -0.22))  # where a rescued survivor sits (faces forward)
        box(f"Buckle_{k}", (x, 1.42, -0.09), (0.07, 0.06, 0.02), "Metal", "Cabin", rot=(70, 0, 0), bevel=0.005)

    # ---------------------------------------------------------------- cargo net on the rear wall (upper left) with bags
    for i in range(6):
        x = -0.85 + i * 0.1
        tube(f"NetV_{i}", [(x, 2.3, -0.43), (x + 0.02, 1.85, -0.36)], 0.006, "Net", "Props", segs=4)
    for j in range(5):
        y = 1.85 + j * 0.11
        tube(f"NetH_{j}", [(-0.86, y, -0.36 - j * 0.017), (-0.35, y, -0.36 - j * 0.017)], 0.006, "Net", "Props", segs=4)
    box("NetBag1", (-0.7, 2.02, -0.44), (0.24, 0.3, 0.12), "Bag", "Props", bevel=0.04)
    box("NetBag2", (-0.47, 1.98, -0.45), (0.18, 0.22, 0.1), "Orange", "Props", bevel=0.03)

    # ---------------------------------------------------------------- first aid kit, extinguisher, headsets, winch box
    box("FirstAidKit", (-0.66, 1.9, 1.39), (0.3, 0.2, 0.1), "FirstAid", "Props", bevel=0.02)
    box("FirstAidCrossH", (-0.66, 1.9, 1.335), (0.12, 0.04, 0.01), "White", "Props", bevel=0.003)
    box("FirstAidCrossV", (-0.66, 1.9, 1.335), (0.04, 0.12, 0.01), "White", "Props", bevel=0.003)
    tube("FirstAidHandle", [(-0.72, 2.0, 1.39), (-0.72, 2.03, 1.39), (-0.6, 2.03, 1.39), (-0.6, 2.0, 1.39)], 0.008, "Black", "Props", segs=4)

    cyl("Extinguisher", (-0.62, 1.3, 1.37), 0.065, 0.42, "FirstAid", "Props", segs=12)
    cyl("ExtinguisherTop", (-0.62, 1.54, 1.37), 0.03, 0.07, "Black", "Props", segs=8)
    box("ExtinguisherLabel", (-0.62, 1.3, 1.306), (0.07, 0.1, 0.004), "White", "Props", bevel=0.001)
    box("ExtinguisherHandle", (-0.62, 1.59, 1.34), (0.02, 0.02, 0.1), "Black", "Props", rot=(20, 0, 0), bevel=0.004)
    tube("ExtinguisherHose", [(-0.6, 1.56, 1.36), (-0.55, 1.5, 1.34), (-0.56, 1.3, 1.32)], 0.01, "Black", "Props", segs=5)
    tube("ExtinguisherStrap", [(-0.69, 1.38, 1.44), (-0.69, 1.38, 1.31), (-0.55, 1.38, 1.31), (-0.55, 1.38, 1.44)], 0.008, "Metal", "Props", segs=4)

    for k, x in enumerate((0.52, 0.74)):  # headsets on hooks, right bulkhead
        tube(f"Hook_{k}", [(x, 2.2, 1.44), (x, 2.2, 1.38), (x, 2.24, 1.37)], 0.007, "Metal", "Props", segs=4)
        tube(f"HeadsetBand_{k}", [(x - 0.09, 1.98, 1.37), (x - 0.08, 2.12, 1.37), (x, 2.19, 1.37), (x + 0.08, 2.12, 1.37), (x + 0.09, 1.98, 1.37)], 0.012, "Black", "Props", segs=6)
        for sx in (-1, 1):
            cyl(f"EarCup_{k}_{sx}", (x + sx * 0.095, 1.95, 1.37), 0.05, 0.05, "Olive", "Props", rot=(0, 0, 90), segs=10)
        tube(f"HeadsetMic_{k}", [(x - 0.11, 1.95, 1.36), (x - 0.12, 1.87, 1.32), (x - 0.07, 1.84, 1.3)], 0.005, "Black", "Props", segs=4)
        cyl(f"HeadsetMicTip_{k}", (x - 0.065, 1.84, 1.3), 0.012, 0.03, "Black", "Props", rot=(0, 0, 90), segs=6)

    # winch control box on the right front door post, facing the door
    box("WinchBox", (0.85, 1.62, 1.28), (0.1, 0.3, 0.2), "Grey", "Props", bevel=0.02)
    cyl("WinchButton", (0.795, 1.66, 1.25), 0.03, 0.03, "RedKnob", "Props", rot=(0, 0, 90), segs=10)
    box("WinchLever", (0.78, 1.55, 1.31), (0.02, 0.1, 0.02), "Black", "Props", rot=(0, 0, 20), bevel=0.004)
    cyl("WinchLeverKnob", (0.77, 1.6, 1.31), 0.018, 0.03, "RedKnob", "Props", segs=8)
    tube("WinchConduit", [(0.86, 1.47, 1.28), (0.88, 1.2, 1.3), (0.9, 1.0, 1.32)], 0.012, "Black", "Props", segs=5)
    marker("M_WinchLightGreen", (0.797, 1.73, 1.33)); marker("M_WinchLightRed", (0.797, 1.73, 1.23))

    # ---------------------------------------------------------------- cockpit shell
    CR = 2.3    # cockpit roof

    # ---------------------------------------------------------------- overhead switch panel
    OY, OZ = 2.23, 2.32
    box("Overhead", (0, OY, OZ), (0.46, 0.1, 0.3), "DarkGrey", "Cockpit", rot=(12, 0, 0), bevel=0.02)
    box("OverheadFace", (0, OY - 0.055, OZ), (0.4, 0.01, 0.25), "PanelFace", "Cockpit", rot=(12, 0, 0), bevel=0.003)
    for k, x in enumerate((-0.14, -0.07, 0.0, 0.07, 0.14)):
        box(f"ToggleBase_{k}", (x, OY - 0.065, OZ - 0.03), (0.04, 0.02, 0.06), "Metal", "Cockpit", rot=(12, 0, 0), bevel=0.004)
        box(f"Toggle_{k}", (x, OY - 0.1, OZ - 0.04), (0.03, 0.07, 0.03), "RedKnob" if k == 2 else "White", "Cockpit", rot=(-40, 0, 0), bevel=0.006)
    marker("M_OverheadLight_0", (-0.16, OY - 0.07, OZ + 0.09)); marker("M_OverheadLight_1", (0.16, OY - 0.07, OZ + 0.09))

    # ---------------------------------------------------------------- dashboard
    DZ = 2.72
    TILT = 18  # the instrument panel leans back so it faces up toward the pilot
    def zf(y):  # z of the panel's front surface at height y
        return DZ - 0.09 + (y - 1.46) * math.tan(math.radians(TILT))
    box("DashBody", (0, 1.27, DZ + 0.14), (1.72, 0.64, 0.34), "DarkGrey", "Cockpit", bevel=0.02)
    box("DashFace", (0, 1.46, DZ - 0.07), (1.64, 0.46, 0.04), "PanelFace", "Cockpit", rot=(TILT, 0, 0), bevel=0.01)
    box("Glareshield", (0, 1.715, DZ + 0.05), (1.72, 0.035, 0.17), "Seat", "Cockpit", rot=(8, 0, 0), bevel=0.012)
    box("DashLower", (0, 1.08, DZ - 0.05), (1.6, 0.26, 0.05), "Grey", "Cockpit", bevel=0.01)
    # gauge housings (faces + needles are added in Unity at the M_Gauge markers)
    gauges = [("Airspeed", 0.23, 1.55), ("Attitude", 0.46, 1.555), ("Altimeter", 0.69, 1.55),
              ("Heading", 0.23, 1.36), ("VSI", 0.46, 1.36), ("RPM", 0.69, 1.36),
              ("Fuel", -0.2, 1.53), ("Temp", -0.2, 1.36), ("Clock", -0.42, 1.53)]
    for name, x, y in gauges:
        r = 0.08 if x > 0 else 0.06
        n = Vector((0, math.sin(math.radians(TILT)), -math.cos(math.radians(TILT))))  # toward the pilot
        c = Vector((x, y, zf(y))) + n * 0.01
        cyl(f"GaugeBezel_{name}", tuple(c), r + 0.012, 0.03, "Black", "Cockpit", rot=(-(90 - TILT), 0, 0), segs=16)
        marker(f"M_Gauge_{name}", tuple(c + n * 0.017)).empty_display_size = r
    # warning light strip
    box("WarningStrip", (0.46, 1.665, zf(1.665) - 0.01), (0.62, 0.05, 0.02), "Black", "Cockpit", rot=(TILT, 0, 0), bevel=0.004)
    for k in range(6):
        marker(f"M_Warn_{k}", (0.2 + k * 0.1, 1.665, zf(1.665) - 0.024))
    # the broken co-pilot gauge, taped over (goofy)
    def dash_tape(name, x, y, length, ang, lift=0.03):
        box(name, (x, y, zf(y) - lift), (length, 0.05, 0.006), "Tape", "Cockpit", rot=(TILT, 0, ang), bevel=0.002)
    dash_tape("DashTapeA", -0.42, 1.36, 0.22, 35, 0.036)   # taped over a broken gauge (drawn in Unity)
    dash_tape("DashTapeB", -0.42, 1.36, 0.22, -35, 0.04)
    dash_tape("DashTapeC", 0.0, 1.62, 0.16, 5, 0.006)
    for k, (x, y) in enumerate(((-0.62, 1.52), (-0.63, 1.33), (0.83, 1.45), (0.03, 1.3))):
        marker(f"M_Note_{k}", (x, y, zf(y) - 0.006))
    marker("M_Gauge_Broken", (-0.42, 1.36, zf(1.36) - 0.03)).empty_display_size = 0.06
    cyl("GaugeBezel_Broken", tuple(Vector((-0.42, 1.36, zf(1.36))) + Vector((0, 0.31, -0.95)) * 0.01), 0.072, 0.03, "Black", "Cockpit", rot=(-(90 - TILT), 0, 0), segs=16)

    # centre pedestal with radios and switches
    box("Pedestal", (0, 1.1, 2.35), (0.3, 0.32, 0.72), "DarkGrey", "Cockpit", bevel=0.02)
    box("PedestalTop", (0, 1.27, 2.35), (0.28, 0.02, 0.7), "PanelFace", "Cockpit", rot=(-6, 0, 0), bevel=0.005)
    for k in range(3):
        z = 2.12 + k * 0.2
        box(f"Radio_{k}", (0, 1.285 + k * 0.02, z), (0.22, 0.02, 0.12), "Black", "Cockpit", rot=(-6, 0, 0), bevel=0.006)
        for sx in (-0.07, 0.07):
            cyl(f"RadioKnob_{k}_{sx}", (sx, 1.305 + k * 0.02, z), 0.018, 0.025, "Metal", "Cockpit", rot=(-6, 0, 0), segs=8)
        marker(f"M_RadioDisplay_{k}", (0, 1.297 + k * 0.02, z))
    tape("PedestalTape", (0.0, 1.29, 2.55), 0.26, 90, "Cockpit")

    # cupholder bracket (right side of the dash) - the can + bobblehead are placed in Unity
    box("CupBracket", (0.82, 1.52, 2.42), (0.14, 0.03, 0.16), "Metal", "Cockpit", bevel=0.006)
    tube("CupRing", [(0.76, 1.6, 2.36), (0.88, 1.6, 2.36), (0.88, 1.6, 2.48), (0.76, 1.6, 2.48), (0.76, 1.6, 2.36)], 0.008, "Metal", "Cockpit", segs=4)
    tube("CupBracketArm", [(0.9, 1.5, 2.42), (0.92, 1.5, 2.42)], 0.02, "Metal", "Cockpit", segs=4)
    marker("M_Can", (0.82, 1.54, 2.42))
    marker("M_Bobblehead", (0.66, 1.77, 2.62))

    # ---------------------------------------------------------------- seats (pilot right, co-pilot left)
    for s, n in ((1, "Pilot"), (-1, "CoPilot")):
        x = s * 0.45
        box(f"{n}SeatBase", (x, 1.12, 1.88), (0.46, 0.3, 0.44), "Seat", "Cockpit", bevel=0.03)
        box(f"{n}SeatCushion", (x, 1.3, 1.9), (0.48, 0.09, 0.46), "Seat", "Cockpit", bevel=0.035)
        box(f"{n}SeatBack", (x, 1.72, 1.64), (0.48, 0.78, 0.1), "Seat", "Cockpit", rot=(-10, 0, 0), bevel=0.035)
        box(f"{n}Headrest", (x, 2.18, 1.56), (0.3, 0.2, 0.1), "Seat", "Cockpit", rot=(-10, 0, 0), bevel=0.04)
        for sx in (-0.11, 0.11):
            box(f"{n}Harness_{sx}", (x + sx, 1.78, 1.69), (0.055, 0.66, 0.012), "Orange" if n == "CoPilot" else "Grey", "Cockpit", rot=(-10, 0, 0), bevel=0.003)
        box(f"{n}HarnessBuckle", (x, 1.4, 2.08), (0.09, 0.09, 0.02), "Metal", "Cockpit", rot=(60, 0, 0), bevel=0.01)
        tape(f"{n}SeatTape", (x + 0.12, 1.9, 1.59), 0.16, 30, "Cockpit")
        marker(f"M_{n}Seat", (x, 0.95, 1.9))
    marker("M_PilotEye", (0.45, 1.98, 1.95))

    # ---------------------------------------------------------------- flight controls (moving parts, pivot at hinge)
    # cyclic: pivots at the floor between the pilot's knees
    pv = (0.45, F + 0.02, 2.22)
    cyl("CyclicBoot", (0.45, F + 0.05, 2.22), 0.06, 0.1, "Rubber", "Cyclic", segs=10, pivot=pv)
    cyl("CyclicShaft", (0.45, F + 0.3, 2.2), 0.018, 0.5, "Black", "Cyclic", rot=(-8, 0, 0), segs=8, pivot=pv)
    box("CyclicGrip", (0.45, F + 0.6, 2.16), (0.05, 0.14, 0.06), "Black", "Cyclic", rot=(-15, 0, 0), bevel=0.02, pivot=pv)
    cyl("CyclicButton", (0.45, F + 0.68, 2.18), 0.012, 0.02, "RedKnob", "Cyclic", segs=8, pivot=pv)
    marker("M_GripCyclic", (0.45, F + 0.6, 2.14), grp="Cyclic").location = to_b((0.45, F + 0.6, 2.14)) - to_b(pv)
    group("Cyclic").location = to_b(pv)
    # collective: hinge beside the pilot's left thigh, lever runs forward
    pc = (0.18, 1.12, 1.72)
    box("CollectiveBase", (0.18, 1.08, 1.72), (0.08, 0.1, 0.12), "DarkGrey", "Collective", bevel=0.02, pivot=pc)
    box("CollectiveLever", (0.18, 1.18, 1.93), (0.035, 0.035, 0.45), "Black", "Collective", rot=(-20, 0, 0), bevel=0.01, pivot=pc)
    cyl("CollectiveGrip", (0.18, 1.25, 2.13), 0.028, 0.14, "Rubber", "Collective", rot=(70, 0, 0), segs=8, pivot=pc)
    box("CollectiveSwitches", (0.18, 1.3, 2.19), (0.05, 0.04, 0.05), "Grey", "Collective", bevel=0.01, pivot=pc)
    cyl("CollectiveButton", (0.18, 1.33, 2.2), 0.01, 0.02, "Yellow", "Collective", segs=6, pivot=pc)
    marker("M_GripCollective", (0.18, 1.26, 2.12), grp="Collective").location = to_b((0.18, 1.26, 2.12)) - to_b(pc)
    group("Collective").location = to_b(pc)
    # pedals
    for n, x in (("PedalL", 0.3), ("PedalR", 0.6)):
        pp = (x, F + 0.05, 2.9)
        box(f"{n}Arm", (x, F + 0.1, 2.9), (0.03, 0.14, 0.03), "Metal", n, bevel=0.006, pivot=pp)
        box(f"{n}Pad", (x, F + 0.16, 2.86), (0.1, 0.03, 0.16), "Black", n, rot=(-55, 0, 0), bevel=0.01, pivot=pp)
        group(n).location = to_b(pp)
    for n in ("Cyclic", "Collective", "PedalL", "PedalR"):
        group(n).parent = group("Interior")

    # ---------------------------------------------------------------- rescue hoist (outside, above the right door)
    # Bolted to the side of the engine deck; the boom sticks out past the door so the cable hangs
    # clear of the sliding door. The cable + hook are built at runtime from M_HoistTip.
    hz = 0.85
    box("HoistMount", (0.73, 2.44, hz), (0.05, 0.3, 0.34), "DarkGrey", "Hoist", bevel=0.015)
    for dz in (-0.12, 0.12):
        for dy in (-0.1, 0.1):
            cyl(f"HoistBolt_{dz}_{dy}", (0.76, 2.44 + dy, hz + dz), 0.014, 0.02, "Metal", "Hoist", rot=(0, 0, 90), segs=6)
    box("HoistBoom", (1.05, 2.53, hz), (0.62, 0.09, 0.11), "Yellow", "Hoist", bevel=0.02)
    tube("HoistBrace", [(0.75, 2.3, hz), (1.22, 2.49, hz)], 0.022, "Grey", "Hoist")
    cyl("HoistDrum", (1.38, 2.5, hz), 0.11, 0.3, "DarkGrey", "Hoist", rot=(90, 0, 0), segs=14)
    cyl("HoistDrumCapF", (1.38, 2.5, hz + 0.16), 0.08, 0.03, "Metal", "Hoist", rot=(90, 0, 0), segs=10)
    cyl("HoistDrumCapB", (1.38, 2.5, hz - 0.16), 0.08, 0.03, "Metal", "Hoist", rot=(90, 0, 0), segs=10)
    box("HoistMotor", (1.21, 2.62, hz), (0.16, 0.1, 0.14), "Grey", "Hoist", bevel=0.02)
    cyl("HoistSheave", (1.4, 2.39, hz), 0.035, 0.04, "Black", "Hoist", rot=(90, 0, 0), segs=10)
    box("HoistStripe0", (0.86, 2.53, hz), (0.05, 0.095, 0.115), "Black", "Hoist", rot=(0, 0, 35), bevel=0)
    box("HoistStripe1", (0.98, 2.53, hz), (0.05, 0.095, 0.115), "Black", "Hoist", rot=(0, 0, 35), bevel=0)
    tape("HoistTape", (1.12, 2.53, hz + 0.058), 0.2, -20, "Hoist")
    marker("M_HoistTip", (1.4, 2.36, hz))
    group("Hoist").parent = group("Interior")

    # ---------------------------------------------------------------- export
    for o in bpy.data.objects: o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE', use_custom_props=False)
    return len([o for o in bpy.data.objects if o.type == 'MESH'])

count = build()
