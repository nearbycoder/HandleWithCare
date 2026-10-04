"""Modeling helpers for Handle With Care (Blender 4.5, bpy + bmesh).

Everything is authored in *Unity* coordinates (x right, y up, z forward = away from the camera)
and converted to Blender space when an object is built. The FBX export settings in `export_fbx`
map Blender (x, y, z) -> Unity (-x, z, -y); UNITY_TO_BLENDER is its inverse.

The game camera looks along +z, so the "front" of every model faces -z.

Material names carry their look so Unity can rebuild them at runtime (ModelLibrary.Resolve):
    col_RRGGBB     painted / plastic / fabric (roughness ~0.6)
    shiny_RRGGBB   glazed ceramic, lacquer
    matte_RRGGBB   foam, felt, paper
    metal_RRGGBB   metallic
    glass_RRGGBB   transparent glass
    glow_RRGGBB    emissive
    tex_NAME       textured (Resources/Textures/NAME.png, NAME_n.png); needs UVs
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

UNITY_TO_BLENDER = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
CELL = 0.25

AXIS_ROT = {
    "x": Matrix.Rotation(math.pi / 2, 4, "Y"),   # local +z -> +x
    "y": Matrix.Rotation(-math.pi / 2, 4, "X"),  # local +z -> +y
    "z": Matrix.Identity(4),
}


def V(*a):
    if len(a) == 1:
        return Vector(a[0])
    return Vector(a)


# ----------------------------------------------------------------------------- materials

def col(h): return "col_" + h.lstrip("#").upper()
def shiny(h): return "shiny_" + h.lstrip("#").upper()
def matte(h): return "matte_" + h.lstrip("#").upper()
def metal(h): return "metal_" + h.lstrip("#").upper()
def glass(h): return "glass_" + h.lstrip("#").upper()
def glow(h): return "glow_" + h.lstrip("#").upper()
def tex(name): return "tex_" + name
def decal(name): return "decal_" + name


def shade(h, factor):
    """Lighten (factor > 1) or darken (factor < 1) a hex colour."""
    h = h.lstrip("#")
    rgb = [int(h[i:i + 2], 16) for i in (0, 2, 4)]
    if factor >= 1:
        rgb = [int(c + (255 - c) * (factor - 1)) for c in rgb]
    else:
        rgb = [int(c * factor) for c in rgb]
    return "".join(f"{max(0, min(255, c)):02X}" for c in rgb)


def _lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


TEXTURE_PREVIEW_COLORS = {"kraft": "C8955A", "kraftin": "D9AE78", "wood": "9C6B43", "tape": "D9B26F",
                          "corrugate": "B98A55", "label": "F3E9D2", "plaster": "E9D8BC"}


def get_material(name):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    if name.startswith("p_"):
        import pbr
        return pbr.build_material(name)
    mat = bpy.data.materials.new(name)
    kind, arg = name.split("_", 1)
    hexstr = TEXTURE_PREVIEW_COLORS.get(arg.split("_")[0], "B0B0B0") if kind in ("tex", "decal") else arg
    rgb = [_lin(int(hexstr[i:i + 2], 16) / 255) for i in (0, 2, 4)]
    roughness = {"metal": 0.3, "glass": 0.05, "glow": 0.5, "shiny": 0.18, "matte": 0.9}.get(kind, 0.6)
    mat.diffuse_color = (*rgb, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Metallic"].default_value = 0.9 if kind == "metal" else 0.0
        bsdf.inputs["Roughness"].default_value = roughness
        if kind == "glow":
            bsdf.inputs["Emission Color"].default_value = (*rgb, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 2.5
        if kind == "glass":
            bsdf.inputs["Transmission Weight"].default_value = 1.0
            bsdf.inputs["Roughness"].default_value = 0.02
            bsdf.inputs["IOR"].default_value = 1.45
            mat.blend_method = "BLEND" if hasattr(mat, "blend_method") else None
            mat.surface_render_method = "BLENDED"
        if kind == "shiny":
            bsdf.inputs["Coat Weight"].default_value = 0.5
    return mat


# ----------------------------------------------------------------------------- primitives
# Every primitive returns (bmesh, smooth_flag) in Unity space; Model.add() merges it.

def _mark_caps_flat(bm, axis_vec):
    for f in bm.faces:
        f.smooth = abs(f.normal.normalized().dot(axis_vec)) < 0.9


def box(mn, mx, bevel=0.0, segments=2):
    mn, mx = V(mn), V(mx)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    size = mx - mn
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=(mn + mx) / 2, verts=bm.verts)
    if bevel > 0:
        b = min(bevel, min(size) * 0.49)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=b, offset_type="OFFSET", segments=segments,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.normal_update()
    return bm, ("auto" if bevel > 0 and segments >= 2 else False)


def cbox(center, size, bevel=0.0, segments=2):
    c, s = V(center), V(size) / 2
    return box(c - s, c + s, bevel, segments)


def cyl(center, radius, length, axis="y", radius2=None, segments=24, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=radius, radius2=radius if radius2 is None else radius2, depth=length)
    if bevel > 0:
        bm.normal_update()
        rim = [e for e in bm.edges if len(e.link_faces) == 2 and
               (abs(e.link_faces[0].normal.z) > 0.9) != (abs(e.link_faces[1].normal.z) > 0.9)]
        bmesh.ops.bevel(bm, geom=rim, offset=bevel, offset_type="OFFSET", segments=3, profile=0.5,
                        affect="EDGES", clamp_overlap=True)
    bm.transform(Matrix.Translation(V(center)) @ AXIS_ROT[axis])
    bm.normal_update()
    if bevel > 0:
        _sharpen(bm, 50)
        for f in bm.faces:
            f.smooth = True
        return bm, None
    _mark_caps_flat(bm, {"x": V(1, 0, 0), "y": V(0, 1, 0), "z": V(0, 0, 1)}[axis])
    return bm, None


def rod(p0, p1, radius, segments=12, radius2=None):
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=d.length)
    rot = V(0, 0, 1).rotation_difference(d.normalized()).to_matrix().to_4x4()
    bm.transform(Matrix.Translation((p0 + p1) / 2) @ rot)
    bm.normal_update()
    _mark_caps_flat(bm, d.normalized())
    return bm, None


def sphere(center, radius, scale=(1, 1, 1), segments=24, rings=14):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    bm.transform(Matrix.Translation(V(center)) @ Matrix.Diagonal(V(*scale, 1.0)))
    bm.normal_update()
    return bm, True


def ellipsoid(center, radii, segments=24, rings=14):
    return sphere(center, 1.0, radii, segments, rings)


def ico(center, radius, subdiv=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    bmesh.ops.translate(bm, vec=V(center), verts=bm.verts)
    bm.normal_update()
    return bm, False


def torus(center, major, minor, axis="y", segments=28, ring_segments=10, arc=1.0, scale=(1, 1, 1), start=0.0):
    """Torus in the plane perpendicular to `axis`. `arc` < 1 gives an open arc starting at `start` (turns)."""
    bm = bmesh.new()
    rings = []
    count = segments if arc >= 1.0 else segments + 1
    for i in range(count):
        a = 2 * math.pi * (start + arc * i / segments)
        ring = []
        for j in range(ring_segments):
            b = 2 * math.pi * j / ring_segments
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    for i in range(len(rings) - (0 if arc >= 1.0 else 1)):
        r0, r1 = rings[i], rings[(i + 1) % len(rings)]
        for j in range(ring_segments):
            bm.faces.new((r0[j], r1[j], r1[(j + 1) % ring_segments], r0[(j + 1) % ring_segments]))
    if arc < 1.0:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bm.transform(Matrix.Translation(V(center)) @ AXIS_ROT[axis] @ Matrix.Diagonal(V(*scale, 1.0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    return bm, True


def lathe(profile, center=(0, 0, 0), axis="y", segments=28, smooth=True):
    """Revolve [(radius, height), ...] (bottom to top) around `axis`. Ends are capped."""
    bm = bmesh.new()
    rings = []
    for r, h in profile:
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new((max(r, 1e-4) * math.cos(a), max(r, 1e-4) * math.sin(a), h)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.transform(Matrix.Translation(V(center)) @ AXIS_ROT[axis])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    if smooth:
        _sharpen(bm, 55)
        for f in bm.faces:
            f.smooth = True
    return bm, None


def shell(profile, center=(0, 0, 0), axis="y", thickness=0.006, segments=28):
    """Hollow surface of revolution open at the top (cups, pots). Outer wall = profile."""
    loop = list(profile) + [(max(0.001, r - thickness), h) for r, h in reversed(profile)]
    bm = bmesh.new()
    rings = []
    for r, h in loop:
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segments), r * math.sin(2 * math.pi * i / segments), h))
                      for i in range(segments)])
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bm.transform(Matrix.Translation(V(center)) @ AXIS_ROT[axis])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    _sharpen(bm, 60)
    for f in bm.faces:
        f.smooth = True
    return bm, None


def sweep(points, radius, closed=False, segments=12, radius_end=None, caps=True, profile_scale=(1, 1)):
    """Tube along a polyline. `radius_end` tapers linearly. profile_scale squashes the cross-section."""
    pts = [V(p) for p in points]
    n = len(pts)
    tangents = []
    for i in range(n):
        if closed:
            t = (pts[(i + 1) % n] - pts[i - 1])
        elif i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
        tangents.append(t.normalized())
    ref = V(0, 0, 1) if abs(tangents[0].z) < 0.9 else V(1, 0, 0)
    normal = (ref - tangents[0] * ref.dot(tangents[0])).normalized()
    bm = bmesh.new()
    rings = []
    for i in range(n):
        if i > 0:
            rot = tangents[i - 1].rotation_difference(tangents[i])
            normal = (rot @ normal).normalized()
        binormal = tangents[i].cross(normal)
        r = radius if radius_end is None else radius + (radius_end - radius) * i / max(1, n - 1)
        ring = []
        for k in range(segments):
            a = 2 * math.pi * k / segments
            ring.append(bm.verts.new(pts[i] + (normal * math.cos(a) * profile_scale[0] + binormal * math.sin(a) * profile_scale[1]) * r))
        rings.append(ring)
    count = n if closed else n - 1
    for i in range(count):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(segments):
            bm.faces.new((r0[k], r0[(k + 1) % segments], r1[(k + 1) % segments], r1[k]))
    if not closed and caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        f.smooth = len(f.verts) == 4
    return bm, None


def bezier(p0, p1, p2, p3, steps=16):
    p0, p1, p2, p3 = V(p0), V(p1), V(p2), V(p3)
    out = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        out.append(p0 * u * u * u + p1 * 3 * u * u * t + p2 * 3 * u * t * t + p3 * t * t * t)
    return out


def spline(points, per_segment=6, closed=False):
    """Catmull-Rom through 2D/3D points: turns a rough profile into a smooth one."""
    pts = [V(*p) if len(p) == 3 else V(p[0], p[1], 0) for p in points]
    dim3 = len(points[0]) == 3
    n = len(pts)
    out = []
    segs = n if closed else n - 1
    for i in range(segs):
        p0 = pts[(i - 1) % n] if closed or i > 0 else pts[0]
        p1, p2 = pts[i], pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if closed or i + 2 < n else pts[-1]
        for k in range(per_segment):
            t = k / per_segment
            t2, t3 = t * t, t * t * t
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append(q)
    if not closed:
        out.append(pts[-1])
    return [tuple(q) if dim3 else (q.x, q.y) for q in out]


def arc_points(center, radius, start_deg, end_deg, plane="xy", steps=12):
    c = V(center)
    out = []
    for i in range(steps + 1):
        a = math.radians(start_deg + (end_deg - start_deg) * i / steps)
        u, v = math.cos(a) * radius, math.sin(a) * radius
        d = {"xy": V(u, v, 0), "xz": V(u, 0, v), "zy": V(0, v, u)}[plane]
        out.append(c + d)
    return out


def hull(points, bevel=0.0, segments=2):
    bm = bmesh.new()
    for p in points:
        bm.verts.new(V(p))
    res = bmesh.ops.convex_hull(bm, input=bm.verts)
    leftovers = list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    if leftovers:
        bmesh.ops.delete(bm, geom=leftovers, context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=0.01, verts=bm.verts, edges=bm.edges)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=segments,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.normal_update()
    return bm, ("auto" if bevel > 0 and segments >= 2 else False)


def prism(points2d, z0, z1, plane="xy", bevel=0.0, segments=2):
    """Extrude a convex 2D outline. plane 'xy' extrudes along z; 'zy' along x; 'xz' along y."""
    pts = []
    for a, b in points2d:
        for depth in (z0, z1):
            if plane == "xy":
                pts.append((a, b, depth))
            elif plane == "zy":
                pts.append((depth, b, a))
            else:
                pts.append((a, depth, b))
    return hull(pts, bevel, segments)


def extrude_outline(points2d, z0, z1, bevel=0.0, segments=2):
    """Extrude any simple (possibly concave) outline in the xy plane between depths z0..z1."""
    bm = bmesh.new()
    front = [bm.verts.new((x, y, z0)) for x, y in points2d]
    back = [bm.verts.new((x, y, z1)) for x, y in points2d]
    n = len(points2d)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], back[i], back[j], front[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=[e for e in bm.edges if not e.is_wire], offset=bevel, offset_type="OFFSET",
                        segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.normal_update()
    return bm, "auto"


def transformed(prim, matrix):
    bm, smooth = prim
    bm.transform(matrix)
    bm.normal_update()
    return bm, smooth


def rotate_about(point, axis, degrees):
    p = V(point)
    return Matrix.Translation(p) @ Matrix.Rotation(math.radians(degrees), 4, axis) @ Matrix.Translation(-p)


def displace(prim, amount, freq=8.0, seed=0, flat=False):
    """Move vertices along their normals by seeded noise (crumpled paper, rocks, cactus)."""
    bm, smooth = prim
    off = V(seed * 13.1, seed * 7.7, seed * 3.3)
    bm.normal_update()
    for v in bm.verts:
        n = noise.noise(v.co * freq + off)
        v.co += v.normal * n * amount
    bm.normal_update()
    return bm, (False if flat else smooth)


def boolean(prim, cutters, op="DIFFERENCE"):
    """Exact mesh boolean of a primitive with a list of cutter primitives."""
    def as_obj(p, name):
        me = bpy.data.meshes.new(name)
        p[0].to_mesh(me)
        p[0].free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob
    base = as_obj(prim, "_bool_base")
    cut_objs = []
    for i, c in enumerate(cutters):
        co = as_obj(c, f"_bool_cut{i}")
        co.hide_render = True
        mod = base.modifiers.new(f"b{i}", "BOOLEAN")
        mod.operation = op
        mod.solver = "EXACT"
        mod.object = co
        cut_objs.append(co)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = base.evaluated_get(dg)
    out = bmesh.new()
    out.from_mesh(ev.to_mesh())
    ev.to_mesh_clear()
    for o in cut_objs + [base]:
        me = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(me)
    out.normal_update()
    return out, prim[1]


def bumps_on_face(center, size, normal_axis, rows, cols, radius, height, segments=10):
    """Grid of little domes on a rectangle (bubble wrap, egg-crate foam). Returns a list of prims."""
    prims = []
    c = V(center)
    for r in range(rows):
        for k in range(cols):
            u = (k + 0.5) / cols - 0.5
            v = (r + 0.5) / rows - 0.5
            if normal_axis == "-z":
                p = c + V(u * size[0], v * size[1], 0)
                prims.append(ellipsoid(p, (radius, radius, height), segments=segments, rings=6))
            elif normal_axis == "y":
                p = c + V(u * size[0], 0, v * size[1])
                prims.append(ellipsoid(p, (radius, height, radius), segments=segments, rings=6))
    return prims


# ----------------------------------------------------------------------------- model

def _sharpen(bm, degrees):
    limit = math.radians(degrees)
    bm.normal_update()
    for e in bm.edges:
        if len(e.link_faces) != 2:
            e.smooth = False
            continue
        e.smooth = e.calc_face_angle(math.pi) < limit


def _uv_box(bm, scale, offset=(0, 0)):
    """Box-projection UVs in Unity space (metres * scale). scale may be (su, sv)."""
    su, sv = (scale, scale) if not isinstance(scale, (tuple, list)) else scale
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co
            if ax == 0:
                u, v = p.z * (1 if n.x > 0 else -1), p.y
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.x * (-1 if n.z > 0 else 1), p.y
            l[uv].uv = (u * su + offset[0], v * sv + offset[1])


class Model:
    """Accumulates primitives (each with one material) into a single mesh object."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.materials = []
        self.bm.loops.layers.uv.verify()

    def add(self, prim, material, smooth=None, transform=None, uv_scale=None, uv_offset=(0, 0)):
        part, default_smooth = prim
        if transform is not None:
            part.transform(transform)
            part.normal_update()
        if material not in self.materials:
            self.materials.append(material)
        index = self.materials.index(material)
        flag = default_smooth if smooth is None else smooth
        for f in part.faces:
            f.material_index = index
            if flag is not None:
                f.smooth = flag is True or flag == "auto"
        if flag == "auto":
            _sharpen(part, 50.0)
        if uv_scale is not None:
            _uv_box(part, uv_scale, uv_offset)
        else:
            part.loops.layers.uv.verify()
        mesh = bpy.data.meshes.new("_part")
        part.to_mesh(mesh)
        part.free()
        self.bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
        return self

    def add_all(self, prims, material, **kw):
        for p in prims:
            self.add(p, material, **kw)
        return self

    def add_fused(self, parts, voxel=0.002, smooth_iters=6, smooth_factor=0.6, max_faces=30000, transform=None):
        """Union overlapping primitives into one watertight, softly filleted mesh (voxel remesh +
        smoothing), like a sculpt. Each face takes the material of the nearest source part."""
        from mathutils.bvhtree import BVHTree
        union = bmesh.new()
        trees, mats = [], []
        for prim, material in parts:
            bm = prim[0]
            if transform is not None:
                bm.transform(transform)
            bm.normal_update()
            trees.append(BVHTree.FromBMesh(bm))
            mats.append(material)
            tmp = bpy.data.meshes.new("_t")
            bm.to_mesh(tmp)
            bm.free()
            union.from_mesh(tmp)
            bpy.data.meshes.remove(tmp)
        me = bpy.data.meshes.new("_fuse")
        union.to_mesh(me)
        union.free()
        ob = bpy.data.objects.new("_fuse", me)
        bpy.context.scene.collection.objects.link(ob)
        r = ob.modifiers.new("remesh", "REMESH")
        r.mode = "VOXEL"
        r.voxel_size = voxel
        r.adaptivity = 0.0
        s = ob.modifiers.new("smooth", "SMOOTH")
        s.factor = smooth_factor
        s.iterations = smooth_iters
        dg = bpy.context.evaluated_depsgraph_get()
        faces = len(ob.evaluated_get(dg).data.polygons)
        if faces > max_faces:
            d = ob.modifiers.new("decimate", "DECIMATE")
            d.ratio = max_faces / faces
            dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        res = bmesh.new()
        res.from_mesh(ev.to_mesh())
        ev.to_mesh_clear()
        bpy.data.objects.remove(ob)
        bpy.data.meshes.remove(me)
        for m in mats:
            if m not in self.materials:
                self.materials.append(m)
        for f in res.faces:
            c = f.calc_center_median()
            best, bd = 0, 1e9
            for i, t in enumerate(trees):
                hit = t.find_nearest(c)
                if hit[0] is not None and hit[3] < bd:
                    best, bd = i, hit[3]
            f.material_index = self.materials.index(mats[best])
            f.smooth = True
        res.loops.layers.uv.verify()
        mesh = bpy.data.meshes.new("_part")
        res.to_mesh(mesh)
        res.free()
        self.bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
        return self

    def build(self, origin=(0, 0, 0), parent=None, parent_origin=(0, 0, 0)):
        """Create the Blender object. Vertices are made relative to `origin` (Unity space)."""
        bm = self.bm
        bmesh.ops.translate(bm, vec=-V(origin), verts=bm.verts)
        bm.transform(UNITY_TO_BLENDER)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)  # the axis swap mirrors, so flip winding
        bm.normal_update()
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for m in self.materials:
            mesh.materials.append(get_material(m))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (UNITY_TO_BLENDER @ (V(origin) - V(parent_origin)).to_4d()).to_3d()
        if parent is not None:
            obj.parent = parent
        return obj


def empty(name, origin=(0, 0, 0), parent=None, parent_origin=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = (UNITY_TO_BLENDER @ (V(origin) - V(parent_origin)).to_4d()).to_3d()
    obj.parent = parent
    return obj


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.objects, bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for block in list(coll):
            coll.remove(block)


def export_fbx(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
        for c in o.children_recursive:
            c.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, object_types={"MESH", "EMPTY"},
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP")


# ----------------------------------------------------------------------------- preview rendering

def setup_preview_scene(resolution=512):
    import os
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes.get("Background")
    hdri = os.path.join(os.path.dirname(bpy.app.binary_path), "4.5", "datafiles", "studiolights", "world", "interior.exr")
    if os.path.exists(hdri):
        env = nt.nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(hdri, check_existing=True)
        nt.links.new(env.outputs["Color"], bg.inputs["Color"])
        bg.inputs[1].default_value = 0.8
    else:
        bg.inputs[0].default_value = (0.62, 0.56, 0.5, 1)
        bg.inputs[1].default_value = 0.9
    scene.world = world
    # tabletop to catch contact shadows (placed under the model in frame_and_render)
    pm = bpy.data.meshes.new("table")
    import bmesh as _bm
    b = _bm.new()
    _bm.ops.create_grid(b, x_segments=1, y_segments=1, size=4.0)
    b.to_mesh(pm)
    b.free()
    pm.materials.append(get_material("p_varnish_9C6B43"))
    table = bpy.data.objects.new("__table", pm)
    scene.collection.objects.link(table)
    # key + fill lights (Blender space: z up)
    for name, loc, energy, color in (("key", (-1.5, -2.0, 2.5), 250, (1, 0.93, 0.85)), ("fill", (2.0, -1.0, 1.0), 60, (0.8, 0.88, 1.0)),
                                     ("rim", (0.5, 2.0, 1.8), 120, (1, 1, 1))):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.size = 1.5
        ld.color = color
        lo = bpy.data.objects.new(name, ld)
        lo.location = loc
        direction = -V(loc)
        lo.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(lo)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 70
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def frame_and_render(cam, objects, path, yaw_deg=-25, pitch_deg=14, margin=1.15):
    """Point the camera at the objects from the game's viewpoint (front = Unity -z) and render."""
    import mathutils
    pts = []
    for o in objects:
        for c in [o] + list(o.children_recursive):
            if c.type == "MESH" and c.name != "__table":
                pts += [c.matrix_world @ v.co for v in c.data.vertices]
    if not pts:
        return
    mn = V(min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))
    mx = V(max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))
    table = bpy.data.objects.get("__table")
    if table:
        table.location = (0, 0, mn.z - 0.0005)
    center = (mn + mx) / 2
    radius = (mx - mn).length / 2
    # Unity front (-z) is Blender +y; camera sits at -? : Unity camera at -z looking +z => Blender (0, +y?)
    # Unity (x,y,z) -> Blender (-x, -z, y): Unity camera at (0, h, -d) -> Blender (0, d, h)
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    d = radius / math.sin(math.radians(cam.data.angle * 57.2958 / 2)) * margin
    dirv = V(math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch))
    cam.location = center + dirv * d
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
