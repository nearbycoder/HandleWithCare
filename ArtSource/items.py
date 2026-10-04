"""Item, padding and packing-material models. One builder per piece; each returns root objects.

Conventions (Unity space, metres): a piece of W x H cells is centred on the origin and fills
x in [-W*0.125, W*0.125], y in [-H*0.125, H*0.125]; the sim insets bodies by 0.0075 vertically,
so models rest on y0 = -H*0.125 + 0.0075. Depth stays within z in [-0.12, 0.12]; the camera
looks along +z, so faces pointing -z are the "front". Creatures face +x (right) by default.

Surfaces use procedural PBR materials (pbr.P) that build_assets bakes into textures; glass_ and
glow_ parts stay runtime materials, and shards/puddle keep plain names because Unity recolours them.
"""
import math
import random

import bmesh
from mathutils import Matrix, Vector, noise

from hwc_lib import (CELL, Model, V, _sharpen, bezier, boolean, box, cbox, cyl, ellipsoid, extrude_outline, glass,
                     glow, hull, lathe, prism, rod, rotate_about, shade, shiny, sphere, spline, sweep, tex, torus)
from pbr import P


def y0(h=1):
    return -h * CELL / 2 + 0.0075


def yaw(deg, center=(0, 0, 0)):
    return rotate_about(center, "Y", deg)


def frame_at(p, z_dir, y_hint=(0, 1, 0)):
    """Matrix placing a primitive built around the origin at p with its local +z along z_dir."""
    z = V(z_dir).normalized()
    x = V(y_hint).cross(z)
    if x.length < 1e-6:
        x = V(1, 0, 0).cross(z)
    x.normalize()
    y = z.cross(x)
    return Matrix.Translation(V(p)) @ Matrix((x, y, z)).transposed().to_4x4()


def squash_about(c, sx=1.0, sy=1.0, sz=1.0):
    c = V(c)
    return Matrix.Translation(c) @ Matrix.Diagonal(V(sx, sy, sz, 1.0)) @ Matrix.Translation(-c)


def revolve(points, base_y, segments=72, per=4):
    """Solid of revolution through spline-smoothed (radius, height) points, heights relative to base_y."""
    return lathe([(r, base_y + h) for r, h in spline(points, per)], segments=segments)


# ============================================================================ items

def _radius_at(profile, h):
    """Interpolated radius of a (r, h) profile at height h (profile must rise monotonically)."""
    for (r0, h0), (r1, h1) in zip(profile, profile[1:]):
        if h0 <= h <= h1 and h1 > h0:
            return r0 + (r1 - r0) * (h - h0) / (h1 - h0)
    return profile[-1][0]


def band(m, outer, h0, h1, material, proud=0.0005, segments=64):
    """A painted/gilded ring hugging a revolved surface between heights h0..h1."""
    ra, rb = _radius_at(outer, h0), _radius_at(outer, h1)
    m.add(lathe([(0.0, h0), (ra - 0.002, h0), (ra + proud, h0 + 0.0004), (rb + proud, h1 - 0.0004), (rb - 0.002, h1), (0.0, h1)],
                segments=segments), material)


def teacup():
    m = Model("piece_teacup")
    b = y0()
    china, gold, blue, tea = P("porcelain", "F8F5EE"), P("gold", "D9A944"), P("porcelain", "3E66A8"), P("liquid", "5A2E12")
    # saucer: foot ring underneath, shallow well, flared rim with a rolled edge
    saucer = spline([(0.0, 0.004), (0.038, 0.004), (0.043, 0.0), (0.05, 0.0), (0.056, 0.005), (0.08, 0.009), (0.103, 0.017),
                     (0.1135, 0.0225), (0.1155, 0.0255), (0.112, 0.0262), (0.102, 0.0215), (0.078, 0.014), (0.052, 0.0108),
                     (0.0, 0.0108)], 4)
    m.add(lathe([(r, b + h) for r, h in saucer], segments=72), china)
    m.add(torus((0, b + 0.0252, 0), 0.1145, 0.0016, segments=72, ring_segments=8), gold)
    cb = b + 0.0108
    outer = [(0.036, 0.0), (0.041, 0.002), (0.043, 0.007), (0.05, 0.013), (0.066, 0.034), (0.079, 0.068), (0.087, 0.105),
             (0.0925, 0.14), (0.0952, 0.156)]
    inner = [(0.0922, 0.155), (0.0892, 0.14), (0.0835, 0.105), (0.0745, 0.068), (0.061, 0.04), (0.044, 0.022), (0.0, 0.0185)]
    prof = [(0.0, 0.0)] + spline(outer, 4) + [(0.0957, 0.1585), (0.0948, 0.1605), (0.0932, 0.1598)] + spline(inner, 4)
    m.add(lathe([(r, cb + h) for r, h in prof], segments=72), china)
    m.add(torus((0, cb + 0.1596, 0), 0.0943, 0.0017, segments=72, ring_segments=8), gold)
    band(m, [(r, cb + h) for r, h in spline(outer, 4)], cb + 0.112, cb + 0.122, blue)
    band(m, [(r, cb + h) for r, h in spline(outer, 4)], cb + 0.127, cb + 0.129, gold)
    m.add(cyl((0, cb + 0.128, 0), 0.0868, 0.002, axis="y", segments=72), tea)
    # handle: oval section, thinner front-to-back, ends sunk into the wall
    path = bezier((0.083, cb + 0.128, 0), (0.135, cb + 0.142, 0), (0.138, cb + 0.052, 0), (0.07, cb + 0.046, 0), 28)
    m.add(sweep(path, 0.0085, radius_end=0.0075, segments=18, profile_scale=(0.62, 1.15)), china)
    return [m.build()]


def books():
    """Three cloth-bound hardbacks: rounded spines, overhanging boards, page blocks, gilt tooling."""
    m = Model("piece_books")
    b = y0()
    gold, pages = P("gold", "D4A84A"), P("pages", "F0E6CC")
    specs = [  # (width, height, depth, x offset, yaw, cloth colour, spine toward camera?)
        (0.462, 0.072, 0.205, 0.0, -1.2, "8A2A23", True),
        (0.43, 0.066, 0.19, 0.01, 1.8, "22385C", False),
        (0.448, 0.07, 0.196, -0.008, -0.6, "2E573A", True),
    ]
    y = b
    for w, h, d, xo, rot, c, front in specs:
        cloth, label = P("cloth", c), P("cloth", shade(c, 0.5))
        t, r, sq = 0.0035, h / 2, 0.32
        x0, x1 = xo - w / 2, xo + w / 2
        sgn = 1 if front else -1           # from the spine into the book
        zs = -d / 2 if front else d / 2    # outer face of the spine
        zc = zs + sgn * r * sq             # spine axis
        fore = d / 2 if front else -d / 2
        tr = yaw(rot, (xo, 0, 0))
        za, zb = sorted((zc, fore))
        m.add(box((x0, y, za), (x1, y + t, zb), bevel=0.0012), cloth, transform=tr)
        m.add(box((x0, y + h - t, za), (x1, y + h, zb), bevel=0.0012), cloth, transform=tr)
        spine_m = tr @ squash_about((xo, y + r, zc), sz=sq)
        m.add(cyl((xo, y + r, zc), r, w, axis="x", segments=48), cloth, transform=spine_m)
        pa, pb = sorted((zc, fore - sgn * 0.005))
        m.add(box((x0 + 0.005, y + t, pa), (x1 - 0.005, y + h - t, pb), bevel=0.001), pages, transform=tr)
        # leather title label and gilt rules across the spine
        m.add(cyl((xo, y + r, zc), r + 0.0009, 0.13, axis="x", segments=48), label, transform=tr @ squash_about((xo, y + r, zc), sz=sq))
        for bx in (xo - 0.066, xo + 0.066, x0 + 0.028, x0 + 0.034, x1 - 0.034, x1 - 0.028):
            m.add(cyl((bx, y + r, zc), r + 0.0016, 0.0022, axis="x", segments=48), gold, transform=tr @ squash_about((xo, y + r, zc), sz=sq))
        y += h + 0.0015
    return [m.build()]


def teddy():
    """Sitting mohair bear: one fused plush body, felt pads, glass eyes, stitched nose, satin bow."""
    m = Model("piece_teddy")
    b = y0()
    fur, muzzle, pad = P("plush", "A8723F"), P("plush", "D9B07E"), P("felt", "E6CFA6")
    parts = [
        (ellipsoid((0, b + 0.07, 0.008), (0.062, 0.07, 0.056), segments=40, rings=24), fur),
        (ellipsoid((0, b + 0.162, -0.002), (0.058, 0.053, 0.052), segments=40, rings=24), fur),
        (ellipsoid((0, b + 0.147, -0.044), (0.03, 0.022, 0.024), segments=32, rings=18), muzzle),
    ]
    for s in (-1, 1):
        ear = (s * 0.045, b + 0.205, 0.002)
        parts.append((ellipsoid(ear, (0.022, 0.021, 0.011), segments=24, rings=14), fur))
        parts.append((ellipsoid((s * 0.046, b + 0.204, -0.0075), (0.0135, 0.013, 0.004), segments=20, rings=10), muzzle))
        arm = bezier((s * 0.042, b + 0.108, -0.004), (s * 0.068, b + 0.095, -0.012), (s * 0.074, b + 0.062, -0.028), (s * 0.066, b + 0.045, -0.04), 12)
        parts.append((sweep(arm, 0.021, radius_end=0.0185, segments=20), fur))
        parts.append((sphere(arm[-1], 0.019, segments=20, rings=12), fur))
        leg = bezier((s * 0.034, b + 0.032, 0.004), (s * 0.04, b + 0.026, -0.02), (s * 0.045, b + 0.026, -0.045), (s * 0.047, b + 0.03, -0.062), 10)
        parts.append((sweep(leg, 0.026, radius_end=0.025, segments=20), fur))
        parts.append((ellipsoid((s * 0.047, b + 0.031, -0.066), (0.025, 0.027, 0.014), segments=24, rings=14), fur))
        parts.append((ellipsoid((s * 0.047, b + 0.031, -0.078), (0.0175, 0.019, 0.004), segments=24, rings=12), pad))
    m.add_fused(parts, voxel=0.0014, smooth_iters=8, smooth_factor=0.5, max_faces=36000)
    # glass eyes, embroidered nose and mouth
    for s in (-1, 1):
        m.add(sphere((s * 0.022, b + 0.172, -0.0485), 0.0072, segments=20, rings=12), P("bead", "120C08"))
    m.add(ellipsoid((0, b + 0.1555, -0.0655), (0.011, 0.0068, 0.0055), segments=20, rings=12), P("thread", "2A1A12"))
    thread = P("thread", "2A1A12")
    m.add(sweep([(0, b + 0.149, -0.0675), (0, b + 0.142, -0.0672)], 0.0011, segments=6), thread)
    for s in (-1, 1):
        m.add(sweep(bezier((0, b + 0.142, -0.0672), (s * 0.004, b + 0.1385, -0.0668), (s * 0.008, b + 0.1385, -0.0655), (s * 0.011, b + 0.1405, -0.0635), 8),
                    0.0011, segments=6), thread)
    # satin ribbon bow at the neck
    bow = P("satin", "B8262E")
    bparts = [(ellipsoid((0, b + 0.117, -0.057), (0.01, 0.011, 0.007), segments=20, rings=12), bow)]
    for s in (-1, 1):
        bparts.append((torus((s * 0.024, b + 0.12, -0.055), 0.017, 0.0048, axis="z", segments=36, ring_segments=10, scale=(1.0, 0.6, 1.0)), bow))
        tail = bezier((s * 0.004, b + 0.112, -0.058), (s * 0.011, b + 0.099, -0.061), (s * 0.016, b + 0.091, -0.06), (s * 0.023, b + 0.083, -0.057), 10)
        bparts.append((sweep(tail, 0.0062, radius_end=0.0056, segments=12, profile_scale=(0.35, 1.0)), bow))
    m.add_fused(bparts, voxel=0.0008, smooth_iters=4, smooth_factor=0.5, max_faces=9000)
    return [m.build()]


def vase():
    """Thrown stoneware vase: teal glaze, white slip band with gilt rules, painted leaves, gilt lip."""
    m = Model("piece_vase")
    b = y0(2)
    glaze, white, gold = P("ceramic", "2B7890"), P("porcelain", "F2EEE4"), P("gold", "D4A84A")
    outer = [(0.05, 0.0), (0.056, 0.003), (0.058, 0.01), (0.055, 0.018), (0.062, 0.03), (0.084, 0.07), (0.1, 0.125), (0.104, 0.168),
             (0.097, 0.228), (0.078, 0.29), (0.056, 0.338), (0.045, 0.372), (0.043, 0.4), (0.049, 0.433), (0.062, 0.455), (0.069, 0.464)]
    inner = [(0.061, 0.463), (0.052, 0.448), (0.04, 0.42), (0.035, 0.39), (0.0, 0.38)]
    so = spline(outer, 5)
    prof = [(0.0, 0.0)] + so + [(0.0712, 0.4672), (0.0695, 0.4702), (0.0655, 0.4695)] + spline(inner, 3)
    m.add(lathe([(r, b + h) for r, h in prof], segments=96), glaze)
    surf = [(r, b + h) for r, h in so]
    band(m, surf, b + 0.156, b + 0.178, white, segments=96)
    band(m, surf, b + 0.148, b + 0.1505, gold, segments=96)
    band(m, surf, b + 0.1835, b + 0.186, gold, segments=96)
    band(m, surf, b + 0.003, b + 0.011, gold, segments=96)
    m.add(torus((0, b + 0.4688, 0), 0.0688, 0.0021, segments=96, ring_segments=10), gold)
    # brushed leaf sprays on the shoulder
    for k in range(10):
        a = (k + 0.5) / 10 * 2 * math.pi
        hh = b + 0.255
        r = _radius_at(surf, hh)
        dr = (_radius_at(surf, hh + 0.01) - _radius_at(surf, hh - 0.01)) / 0.02
        radial = V(math.cos(a), 0, math.sin(a))
        up = V(radial.x * dr, 1, radial.z * dr).normalized()
        normal = (radial - up * radial.dot(up)).normalized()
        p = V(math.cos(a) * r, hh, math.sin(a) * r)
        for tilt in (-28, 28):
            leaf_up = (Matrix.Rotation(math.radians(tilt), 3, normal) @ up)
            m.add(ellipsoid((0, 0.018, 0), (0.0065, 0.019, 0.0011), segments=16, rings=8), white,
                  transform=frame_at(p, normal, leaf_up))
        m.add(ellipsoid((0, 0, 0), (0.0032, 0.0032, 0.0011), segments=10, rings=6), gold, transform=frame_at(p + up * 0.042, normal))
    return [m.build()]


def bowling():
    """Reactive-resin bowling ball with drilled finger holes."""
    m = Model("piece_bowling")
    b = y0()
    r = 0.112
    c = V(0, b + r, 0)
    cutters = []
    for dx, dy, hr in ((-0.024, 0.046, 0.0105), (0.024, 0.046, 0.0105), (0.0, -0.014, 0.0128)):
        d = V(dx, dy, -1).normalized()
        cutters.append(rod(c + d * (r - 0.05), c + d * (r + 0.02), hr, segments=40))
    ball = boolean(sphere(c, r, segments=128, rings=64), cutters)
    m.add(ball, P("swirl", "2A1446", "8256C2"), smooth="auto")
    return [m.build()]


def _armadillo_shell(body, radii, n_bands, step, shell, ridge, segs=56):
    parts = [(ellipsoid(body, radii, segments=segs, rings=32), shell)]
    for k in range(-(n_bands // 2), n_bands // 2 + 1):
        x = k * step
        f = 1 - (x / radii[0]) ** 2
        if f < 0.25:
            continue
        parts.append((torus(V(body) + V(x, 0, 0), radii[1] * math.sqrt(f), 0.0042, axis="x", segments=64, ring_segments=10,
                            scale=(radii[2] / radii[1], 1, 1)), ridge))
    return parts


def armadillo():
    """Nine-banded armadillo rolled into a ball: banded carapace, head shield and tail tucked in."""
    m = Model("piece_armadillo")
    b = y0()
    shell, ridge, skin = P("armour", "8C7B63"), P("armour", "675846"), P("flesh", "D4A08B")
    c = V(0, b + 0.1, 0)
    parts = _armadillo_shell(c, (0.106, 0.1, 0.094), 9, 0.02, shell, ridge)
    head = c + V(0.072, -0.045, -0.05)
    parts.append((ellipsoid(head, (0.03, 0.026, 0.028), segments=32, rings=20), shell))
    snout = bezier(head + V(0.01, -0.005, -0.012), head + V(0.03, -0.015, -0.025), head + V(0.04, -0.03, -0.03), head + V(0.042, -0.042, -0.032), 10)
    parts.append((sweep(snout, 0.014, radius_end=0.0065, segments=20), skin))
    parts.append((sphere(snout[-1], 0.0068, segments=16, rings=10), skin))
    for s in (-1, 1):
        parts.append((ellipsoid(head + V(-0.012, 0.024, s * 0.016 - 0.004), (0.007, 0.015, 0.0035), segments=16, rings=10), skin))
    tail = bezier(c + V(-0.07, -0.068, -0.052), c + V(-0.03, -0.09, -0.062), c + V(0.01, -0.092, -0.064), c + V(0.036, -0.084, -0.062), 14)
    parts.append((sweep(tail, 0.0135, radius_end=0.006, segments=18), ridge))
    m.add_fused(parts, voxel=0.0013, smooth_iters=5, smooth_factor=0.5, max_faces=40000)
    eye = head + V(0.012, 0.006, -0.0268)
    m.add(sweep(bezier(eye + V(-0.005, 0, 0), eye + V(-0.002, -0.003, -0.0006), eye + V(0.002, -0.003, -0.0006), eye + V(0.005, 0, 0), 8),
                0.0009, segments=6), P("thread", "2A1C16"))
    return [m.build()]


def armadillo_awake():
    """Uncurled and grumpy (shown in the reveal when it woke up)."""
    m = Model("piece_armadillo_awake")
    b = y0()
    shell, ridge, skin, claw = P("armour", "8C7B63"), P("armour", "675846"), P("flesh", "D4A08B"), P("wax", "3A2E26")
    body = V(-0.005, b + 0.072, 0)
    parts = _armadillo_shell(body, (0.088, 0.062, 0.06), 7, 0.016, shell, ridge)
    parts.append((ellipsoid(body + V(0, -0.026, 0), (0.074, 0.034, 0.05), segments=40, rings=20), skin))
    hd = body + V(0.082, 0.012, -0.004)
    parts.append((ellipsoid(hd, (0.03, 0.026, 0.026), segments=32, rings=20), shell))
    snout = bezier(hd + V(0.015, -0.004, 0), hd + V(0.03, -0.01, -0.002), hd + V(0.042, -0.022, -0.003), hd + V(0.048, -0.03, -0.004), 10)
    parts.append((sweep(snout, 0.011, radius_end=0.005, segments=18), skin))
    parts.append((sphere(snout[-1], 0.0052, segments=14, rings=8), skin))
    for s in (-1, 1):
        e = hd + V(-0.008, 0.027, s * 0.014)
        parts.append((ellipsoid(e, (0.008, 0.016, 0.0035), segments=16, rings=10), skin))
        for lx in (0.05, -0.05):
            top = body + V(lx, -0.03, s * 0.034)
            foot = V(body.x + lx + 0.006, b + 0.007, s * 0.038)
            parts.append((sweep([top, (top + foot) / 2 + V(0.002, 0, 0), foot], 0.012, radius_end=0.0105, segments=16), skin))
            parts.append((ellipsoid(foot + V(0.006, 0, 0), (0.014, 0.0075, 0.011), segments=20, rings=10), skin))
    tail = bezier(body + V(-0.08, -0.012, 0), body + V(-0.102, -0.025, 0.01), body + V(-0.114, -0.05, 0.025), body + V(-0.118, -0.064, 0.04), 14)
    parts.append((sweep(tail, 0.014, radius_end=0.0045, segments=16), ridge))
    m.add_fused(parts, voxel=0.0012, smooth_iters=5, smooth_factor=0.5, max_faces=40000)
    for s in (-1, 1):
        for lx in (0.05, -0.05):
            toe = V(body.x + lx + 0.019, b + 0.004, s * 0.038)
            for dz in (-0.006, 0, 0.006):
                m.add(rod(toe + V(0, 0, dz), toe + V(0.008, -0.003, dz), 0.0018, radius2=0.0004, segments=6), claw)
    m.add(sphere(hd + V(0.01, 0.009, -0.0245), 0.0045, segments=16, rings=10), P("bead", "120C0A"))
    m.add(sweep([hd + V(0.002, 0.019, -0.024), hd + V(0.012, 0.0155, -0.0262), hd + V(0.02, 0.012, -0.024)], 0.0012, segments=6),
          P("thread", "2A1C16"))
    return [m.build()]


def magnet():
    """Horseshoe magnet: red enamelled steel with ground pole pieces."""
    m = Model("piece_magnet")
    b = y0()
    red, steel = P("lacquer", "C42B24"), P("brushed", "C8CCD1")
    cy = b + 0.13
    R, r = 0.062, 0.03
    parts = [(torus((0, cy, 0), R, r, axis="z", arc=0.5, start=0.0, segments=64, ring_segments=28), red)]
    for s in (-1, 1):
        parts.append((cyl((s * R, cy - 0.045, 0), r, 0.09, axis="y", segments=32), red))
    m.add_fused(parts, voxel=0.0011, smooth_iters=3, smooth_factor=0.4, max_faces=30000)
    for s in (-1, 1):
        m.add(cyl((s * R, b + 0.0215, 0), r * 1.005, 0.043, axis="y", segments=48, bevel=0.0025), steel)
    return [m.build()]


def potion():
    """Blown-glass flask of glowing potion, cork stopper under a dripping wax seal, paper tag."""
    m = Model("piece_potion")
    b = y0(2)
    outer = [(0.05, 0.0), (0.075, 0.012), (0.095, 0.045), (0.103, 0.09), (0.096, 0.14), (0.07, 0.18), (0.042, 0.205), (0.032, 0.225),
             (0.03, 0.33), (0.033, 0.352), (0.04, 0.36), (0.0405, 0.37), (0.035, 0.374)]
    m.add(lathe([(0.0, b)] + [(r, b + h) for r, h in spline(outer, 4)] + [(0.0, b + 0.374)], segments=72), glass("DCCFF2"))
    m.add(revolve([(0.0, 0.008), (0.05, 0.01), (0.08, 0.03), (0.094, 0.08), (0.089, 0.124), (0.0, 0.126)], b, 64, 3), glow("8E4FC4"))
    for x, yy, rr in ((0.02, 0.15, 0.01), (-0.015, 0.175, 0.007), (0.005, 0.24, 0.006), (-0.006, 0.29, 0.005), (0.03, 0.11, 0.008)):
        m.add(sphere((x, b + yy, -0.01), rr, segments=14, rings=8), glow("C9A5F2"))
    m.add(revolve([(0.0, 0.35), (0.0265, 0.35), (0.0295, 0.37), (0.033, 0.4), (0.0345, 0.412), (0.0, 0.414)], b, 48, 3), P("cork", "B8905E"))
    wax = P("wax", "8C1C26")
    wparts = [(ellipsoid((0, b + 0.414, 0), (0.0385, 0.011, 0.0385), segments=48, rings=16), wax)]
    for a, ln in ((0.4, 0.024), (1.5, 0.038), (2.6, 0.018), (3.9, 0.03), (5.1, 0.022)):
        top = V(math.cos(a) * 0.035, b + 0.412, math.sin(a) * 0.035)
        end = V(math.cos(a) * 0.0365, b + 0.412 - ln, math.sin(a) * 0.0365)
        wparts.append((sweep([top, (top + end) / 2 + V(math.cos(a), 0, math.sin(a)) * 0.0008, end], 0.0048, radius_end=0.0042, segments=12), wax))
        wparts.append((sphere(end, 0.0054, segments=12, rings=8), wax))
    m.add_fused(wparts, voxel=0.0008, smooth_iters=4, smooth_factor=0.5, max_faces=12000)
    string = P("thread", "9A7B55")
    m.add(torus((0, b + 0.338, 0), 0.0318, 0.0016, segments=48, ring_segments=6), string)
    tag_c = V(0.074, b + 0.258, -0.04)
    m.add(sweep(spline([(0.028, b + 0.336, -0.014), (0.05, b + 0.315, -0.03), (0.064, b + 0.29, -0.038), (tag_c.x - 0.003, tag_c.y + 0.02, tag_c.z)], 4),
                0.0013, segments=6), string)
    tilt = rotate_about(tag_c, "Z", -12)
    m.add(cbox(tag_c, (0.04, 0.048, 0.0012), bevel=0.0005), P("paper", "EDE0C4"), transform=tilt)
    m.add(torus(tag_c + V(-0.003, 0.0185, -0.0007), 0.0042, 0.0011, axis="z", segments=20, ring_segments=6), P("paper", "CDB78F"), transform=tilt)
    return [m.build()]


def _rosette(p, r, h, twist=7.0):
    """Star-tip piped frosting swirl."""
    bm, sm = lathe([(0.0, 0.0), (r, 0.0), (r * 1.02, h * 0.25), (r * 0.82, h * 0.55), (r * 0.45, h * 0.8), (r * 0.12, h * 0.97), (0.0, h)],
                   segments=64)
    for v in bm.verts:
        a = math.atan2(v.co.z, v.co.x)
        k = 1 + 0.2 * math.cos(8 * a + twist * v.co.y / h)
        v.co.x *= k
        v.co.z *= k
    bm.transform(Matrix.Translation(V(p)))
    bm.normal_update()
    return bm, sm


def cake():
    """Celebration cake on a china plate: buttercream, dripping pink glaze, piped rosettes, candles."""
    m = Model("piece_cake")
    b = y0()
    china, gold = P("porcelain", "F7F4EE"), P("gold", "D4A84A")
    cream, pink = P("frosting", "F6E9D8"), P("frosting", "EC86A3")
    plate = [(0.0, 0.0), (0.12, 0.0), (0.125, 0.004), (0.13, 0.004), (0.19, 0.006), (0.226, 0.013), (0.235, 0.017), (0.232, 0.019),
             (0.22, 0.016), (0.19, 0.0125), (0.0, 0.0115)]
    m.add(revolve(plate, b, 128, 4), china)
    m.add(torus((0, b + 0.0178, 0), 0.2335, 0.0012, segments=128, ring_segments=6), gold)
    pt = b + 0.0115
    m.add(revolve([(0.0, 0.0), (0.185, 0.0), (0.188, 0.004), (0.19, 0.06), (0.19, 0.105), (0.0, 0.106)], pt, 128, 3), cream)
    rnd = random.Random(3)
    gparts = [(revolve([(0.0, 0.124), (0.18, 0.125), (0.189, 0.122), (0.1935, 0.113), (0.191, 0.104), (0.0, 0.104)], pt, 128, 3), pink)]
    for k in range(20):
        a = (k + rnd.uniform(-0.25, 0.25)) / 20 * 2 * math.pi
        ln = rnd.uniform(0.014, 0.05)
        top = V(math.cos(a) * 0.1915, pt + 0.11, math.sin(a) * 0.1915)
        end = V(math.cos(a) * 0.1925, pt + 0.11 - ln, math.sin(a) * 0.1925)
        gparts.append((sweep([top, (top + end) / 2, end], 0.0068, radius_end=0.0062, segments=12), pink))
        gparts.append((sphere(end, 0.0078, segments=14, rings=8), pink))
    m.add_fused(gparts, voxel=0.0012, smooth_iters=4, smooth_factor=0.5, max_faces=40000)
    for k in range(48):  # piped bead border at the foot
        a = k / 48 * 2 * math.pi
        m.add(sphere((math.cos(a) * 0.19, pt + 0.007, math.sin(a) * 0.19), 0.0085, scale=(1, 0.85, 1), segments=16, rings=10), cream)
    pearl = P("chrome", "E6E2DA")
    for k in range(10):
        a = k / 10 * 2 * math.pi + 0.3
        p = V(math.cos(a) * 0.158, pt + 0.124, math.sin(a) * 0.158)
        m.add(_rosette(p, 0.017, 0.026), cream)
        m.add(sphere(p + V(0, 0.027, 0), 0.003, segments=10, rings=6), pearl)
    for x, z, c1 in ((-0.08, 0.02, "5C9BD6"), (0.0, -0.01, "F2C94C"), (0.08, 0.02, "6CC07A")):
        base = pt + 0.124
        m.add(cyl((x, base + 0.031, z), 0.0065, 0.062, axis="y", segments=24, bevel=0.0012), P("stripes", "F6F1E7", c1))
        m.add(rod((x, base + 0.061, z), (x, base + 0.069, z), 0.0009, segments=6), P("thread", "1E1A16"))
        m.add(revolve([(0.0, 0.0), (0.0048, 0.004), (0.0058, 0.009), (0.0038, 0.016), (0.0, 0.026)], base + 0.064, 20, 3),
              glow("FFC24A"), transform=Matrix.Translation((x, 0, z)))
    m.add(sphere((0.035, pt + 0.141, -0.07), 0.016, segments=32, rings=18), P("lacquer", "A8121E"))
    m.add(sweep(bezier((0.035, pt + 0.155, -0.07), (0.037, pt + 0.17, -0.072), (0.044, pt + 0.18, -0.074), (0.052, pt + 0.186, -0.075), 8),
                0.0014, segments=6), P("matte", "5A6B2E"))
    return [m.build()]


def balloon():
    """Latex party balloon with a tied neck; its ribbon curls on the floor."""
    m = Model("piece_balloon")
    b = y0()
    latex = P("lacquer", "D42A22")
    c = V(0, b + 0.13, 0)
    m.add(revolve([(0.0, -0.104), (0.012, -0.102), (0.035, -0.092), (0.066, -0.064), (0.086, -0.02), (0.09, 0.025), (0.08, 0.068),
                   (0.055, 0.096), (0.022, 0.108), (0.0, 0.11)], c.y, 96, 5), latex)
    m.add(revolve([(0.0, -0.104), (0.0075, -0.106), (0.0095, -0.111), (0.0055, -0.116), (0.0085, -0.12), (0.0055, -0.1225), (0.0, -0.123)],
                  c.y, 32, 3), latex)
    knot = c + V(0, -0.12, 0)
    pts = spline([tuple(knot), (0.012, b + 0.004, -0.012), (0.045, b + 0.0015, -0.03), (0.07, b + 0.0015, -0.005), (0.055, b + 0.0015, 0.025),
                  (0.03, b + 0.0015, 0.01), (0.05, b + 0.0015, -0.02), (0.09, b + 0.0015, -0.045)], 6)
    m.add(sweep(pts, 0.0013, segments=6), P("satin", "EDE3CF"))
    return [m.build()]


def _ribbed(bm, cx, cz, ribs, depth):
    """Pleat a revolved column into cactus ribs: round crests, V valleys."""
    for v in bm.verts:
        dx, dz = v.co.x - cx, v.co.z - cz
        a = math.atan2(dz, dx)
        k = 1 - depth * (1 - abs(math.cos(ribs / 2 * a)))
        v.co.x = cx + dx * k
        v.co.z = cz + dz * k
    bm.normal_update()


def cactus():
    """Ribbed saguaro-style cactus with areoles and spines in a terracotta pot, flowering."""
    m = Model("piece_cactus")
    b = y0()
    m.add(lathe([(0.0, b), (0.05, b), (0.053, b + 0.002), (0.055, b + 0.006), (0.067, b + 0.062), (0.0675, b + 0.0645),
                 (0.0805, b + 0.066), (0.0832, b + 0.069), (0.0835, b + 0.087), (0.0815, b + 0.0905), (0.0715, b + 0.0905),
                 (0.0695, b + 0.087), (0.0, b + 0.086)], segments=96), P("terracotta", "B9623A"))
    m.add(lathe([(0.0, b + 0.082), (0.0698, b + 0.082), (0.0698, b + 0.0865), (0.04, b + 0.088), (0.0, b + 0.0885)], segments=64),
          P("soil", "3E2C1F"))
    rnd = random.Random(11)
    for k in range(14):
        a, rr = rnd.uniform(0, 2 * math.pi), rnd.uniform(0.052, 0.064)
        peb = sphere((math.cos(a) * rr, b + 0.0875, math.sin(a) * rr), rnd.uniform(0.0035, 0.0065), scale=(1, 0.6, 0.85), segments=12, rings=8)
        m.add(peb, P("matte", rnd.choice(["BDB5A6", "8E8A80", "D8CFBE"])))
    green, felt, spine = P("cactus", "4F8A3C"), P("felt", "EEE7D6"), P("wax", "F1E6C6")
    H0, HT = b + 0.08, 0.145

    def radius(t):
        return 0.046 * max(0.0, math.sin(math.pi * (0.06 + 0.94 * t))) ** 0.45

    prof = [(radius(i / 40), H0 + HT * i / 40) for i in range(41)]
    bm, sm = lathe(prof, segments=160)
    _ribbed(bm, 0, 0, 10, 0.13)
    m.add((bm, sm), green)
    arms = []
    for s, h, ln in ((-1, 0.14, 0.05), (1, 0.165, 0.04)):
        base = V(s * 0.02, b + h, 0)
        knee = V(s * 0.066, b + h + 0.012, 0)
        m.add(sweep(bezier(base, base + V(s * 0.03, 0, 0), knee + V(-s * 0.012, -0.012, 0), knee, 10), 0.0165, segments=24), green)
        ar = [(0.0168 * max(0.0, math.sin(math.pi * (0.5 + 0.5 * i / 16))) ** 0.5, knee.y + ln * i / 16) for i in range(17)]
        abm, asm = lathe(ar, center=(knee.x, 0, 0), segments=96)
        _ribbed(abm, knee.x, 0, 8, 0.12)
        m.add((abm, asm), green)
        arms.append((knee, ln))

    def areole(p, out):
        m.add(sphere(p, 0.0026, segments=10, rings=6), felt)
        side = out.cross(V(0, 1, 0)).normalized()
        for d in (out + V(0, 0.5, 0), out + V(0, -0.45, 0) + side * 0.4, out - side * 0.5):
            d = d.normalized()
            m.add(rod(p, p + d * rnd.uniform(0.009, 0.013), 0.0006, radius2=0.0001, segments=5), spine)

    for rib in range(10):
        a = rib * math.pi / 5
        out = V(math.cos(a), 0, math.sin(a))
        for j in range(8):
            t = 0.1 + j * 0.105 + (0.05 if rib % 2 else 0.0)
            if t > 0.93:
                continue
            p = V(0, H0 + HT * t, 0) + out * (radius(t) + 0.0006)
            areole(p, (out + V(0, 0.25 if t > 0.75 else 0, 0)).normalized())
    for knee, ln in arms:
        for rib in range(8):
            a = rib * math.pi / 4
            out = V(math.cos(a), 0, math.sin(a))
            for j in range(3):
                p = knee + V(0, ln * (0.2 + j * 0.28), 0) + out * 0.0172
                areole(p, out)
    top = V(0, H0 + HT - 0.002, 0)
    petal = P("satin", "EE5D8F")
    for ring, (n, tilt, ln) in enumerate(((9, 35, 0.011), (7, 60, 0.009))):
        for k in range(n):
            a = (k + ring * 0.5) / n * 2 * math.pi
            d = V(math.cos(a) * math.cos(math.radians(tilt)), math.sin(math.radians(tilt)), math.sin(a) * math.cos(math.radians(tilt)))
            m.add(ellipsoid((0, 0, ln), (0.0045, 0.0013, ln), segments=12, rings=8), petal, transform=frame_at(top, d))
    m.add(sphere(top + V(0, 0.004, 0), 0.0045, scale=(1, 0.6, 1), segments=14, rings=8), P("felt", "F6D04D"))
    return [m.build()]


def robot():
    """Wind-up tinplate robot: lithographed panels, rivets, domed eyes, chrome trim, brass key."""
    m = Model("piece_robot")
    b = y0()
    tin, red, navy = P("tin", "B9C3CC"), P("tin", "C3352B"), P("tin", "2F3E5C")
    chrome, gold, dark, dial = P("chrome", "D0D4DA"), P("gold", "D9A944"), P("paint", "22262C"), P("paint", "F2EDE0")
    turn = yaw(32, (0, 0, 0))

    def add(prim, mat):
        m.add(prim, mat, transform=turn)

    add(box((-0.05, b + 0.04, -0.042), (0.05, b + 0.128, 0.042), bevel=0.01, segments=4), tin)
    add(box((-0.0505, b + 0.04, -0.0425), (0.0505, b + 0.051, 0.0425), bevel=0.004, segments=3), navy)
    add(box((-0.034, b + 0.06, -0.0448), (0.034, b + 0.114, -0.04), bevel=0.004, segments=3), red)
    add(cyl((0.016, b + 0.099, -0.0455), 0.0095, 0.003, axis="z", segments=32, bevel=0.001), chrome)
    add(cyl((0.016, b + 0.099, -0.047), 0.0078, 0.0012, axis="z", segments=32), dial)
    add(box((0.0155, b + 0.099, -0.0478), (0.0165, b + 0.106, -0.0474)), dark)
    for k, c in enumerate(("F2D24A", "7EE07A", "FF6A4A")):
        add(sphere((-0.02, b + 0.106 - k * 0.0135, -0.0452), 0.0042, scale=(1, 1, 0.7), segments=14, rings=8), glow(c))
    for x in (-0.04, -0.02, 0.0, 0.02, 0.04):
        for yy in (b + 0.058, b + 0.121):
            add(sphere((x, yy, -0.0425), 0.0017, scale=(1, 1, 0.6), segments=8, rings=5), chrome)
    add(cyl((0, b + 0.131, 0), 0.016, 0.009, axis="y", segments=32, bevel=0.002), chrome)
    add(box((-0.04, b + 0.134, -0.036), (0.04, b + 0.2, 0.036), bevel=0.01, segments=4), tin)
    for s in (-1, 1):
        e = V(s * 0.017, b + 0.172, -0.0362)
        add(torus(e, 0.0112, 0.0022, axis="z", segments=32, ring_segments=8), chrome)
        add(cyl(e + V(0, 0, -0.0004), 0.0094, 0.0012, axis="z", segments=24), glow("FFD24A"))
        add(cyl(e + V(0, 0, -0.0012), 0.0034, 0.0006, axis="z", segments=16), dark)
        add(sphere(e + V(0, 0, -0.0005), 0.0096, scale=(1, 1, 0.45), segments=24, rings=10), glass("FFF6DC"))
        add(cyl((s * 0.0415, b + 0.168, 0), 0.0095, 0.006, axis="x", segments=24, bevel=0.0015), chrome)
        add(sphere((s * 0.055, b + 0.115, 0), 0.0125, segments=20, rings=12), navy)
        arm = bezier((s * 0.055, b + 0.115, 0), (s * 0.066, b + 0.1, -0.003), (s * 0.074, b + 0.084, -0.008), (s * 0.076, b + 0.072, -0.011), 10)
        add(sweep(arm, 0.0088, segments=16), tin)
        add(torus((s * 0.078, b + 0.062, -0.012), 0.0115, 0.0038, axis="z", arc=0.72, start=0.64, segments=20, ring_segments=8), chrome)
        add(box((s * 0.03 - 0.013, b + 0.012, -0.022), (s * 0.03 + 0.013, b + 0.042, 0.02), bevel=0.004, segments=3), navy)
        add(box((s * 0.03 - 0.016, b, -0.033), (s * 0.03 + 0.016, b + 0.014, 0.024), bevel=0.005, segments=3), red)
    add(box((-0.019, b + 0.144, -0.0372), (0.019, b + 0.156, -0.0352), bevel=0.0015), chrome)
    for k in range(6):
        x = -0.0145 + k * 0.0058
        add(box((x - 0.0012, b + 0.146, -0.0378), (x + 0.0012, b + 0.154, -0.0368)), dark)
    add(rod((0, b + 0.2, 0), (0, b + 0.223, 0), 0.0018, segments=10), chrome)
    add(sphere((0, b + 0.2265, 0), 0.0062, segments=16, rings=10), glow("FF5A3A"))
    add(rod((0, b + 0.088, 0.042), (0, b + 0.088, 0.064), 0.0035, segments=12), gold)
    for s in (-1, 1):
        add(torus((s * 0.012, b + 0.088, 0.066), 0.011, 0.0035, axis="z", segments=24, ring_segments=8, scale=(1, 0.75, 1)), gold)
    return [m.build()]


def iceswan():
    """Carved ice swan on an ice plinth (one fused, softly melted form)."""
    m = Model("piece_iceswan")
    b = y0(2)
    ice, deep = glass("CBEAF5"), glass("A6D8EA")
    m.add(box((-0.1, b, -0.075), (0.1, b + 0.034, 0.075), bevel=0.009, segments=3), deep)
    body = V(-0.015, b + 0.1, 0)
    parts = [(ellipsoid(body, (0.085, 0.058, 0.064), segments=48, rings=28), ice)]
    for s in (-1, 1):
        for k in range(4):
            w = body + V(-0.015 - k * 0.012, 0.028 + k * 0.006, s * (0.052 - k * 0.004))
            parts.append((ellipsoid(w, (0.068 - k * 0.01, 0.03 - k * 0.004, 0.012), segments=32, rings=16), ice))
    neck = bezier(body + V(0.06, 0.02, 0), body + V(0.11, 0.08, 0), body + V(0.0, 0.17, 0), body + V(0.06, 0.29, 0), 24)
    parts.append((sweep(neck, 0.021, radius_end=0.015, segments=20), ice))
    head = neck[-1] + V(0.01, 0.012, 0)
    parts.append((ellipsoid(head, (0.03, 0.024, 0.022), segments=28, rings=18), ice))
    parts.append((rod(head + V(0.018, -0.004, 0), head + V(0.062, -0.014, 0), 0.0095, radius2=0.002, segments=16), ice))
    parts.append((sweep(bezier(body + V(-0.08, 0.0, 0), body + V(-0.11, 0.02, 0), body + V(-0.11, 0.05, 0), body + V(-0.09, 0.065, 0), 12),
                        0.016, radius_end=0.004, segments=16), ice))
    parts.append((cbox(body + V(0, -0.06, 0), (0.12, 0.02, 0.09), bevel=0.008), ice))
    m.add_fused(parts, voxel=0.0018, smooth_iters=6, smooth_factor=0.6, max_faces=30000)
    return [m.build()]


def lavalamp():
    """Lava lamp: spun-chrome base and cap, tapered glass, merging wax blobs."""
    m = Model("piece_lavalamp")
    b = y0(2)
    chrome = P("chrome", "D3D7DC")
    m.add(revolve([(0.0, 0.0), (0.074, 0.0), (0.0765, 0.004), (0.074, 0.012), (0.06, 0.04), (0.047, 0.095), (0.046, 0.105), (0.0, 0.105)], b, 96, 3), chrome)
    m.add(torus((0, b + 0.105, 0), 0.0465, 0.0022, segments=96, ring_segments=8), chrome)
    m.add(revolve([(0.0, 0.104), (0.046, 0.104), (0.058, 0.16), (0.062, 0.2), (0.054, 0.28), (0.04, 0.35), (0.034, 0.385), (0.0, 0.385)], b, 72, 4),
          glass("FFC27A"))
    lava = glow("F28A2E")
    blobs = [((0.0, 0.118, 0.0), (0.044, 0.016, 0.044)), ((0.004, 0.15, -0.004), (0.022, 0.026, 0.022)), ((0.0, 0.135, 0.0), (0.01, 0.02, 0.01)),
             ((0.012, 0.205, -0.004), (0.02, 0.026, 0.02)), ((-0.014, 0.262, -0.004), (0.015, 0.019, 0.015)),
             ((0.006, 0.322, -0.004), (0.011, 0.014, 0.011)), ((-0.018, 0.172, -0.004), (0.012, 0.015, 0.012))]
    m.add_fused([(ellipsoid((x, b + y, z), r, segments=28, rings=16), lava) for (x, y, z), r in blobs],
                voxel=0.0015, smooth_iters=6, smooth_factor=0.6, max_faces=12000)
    m.add(revolve([(0.0, 0.38), (0.036, 0.38), (0.0345, 0.39), (0.025, 0.455), (0.022, 0.462), (0.0, 0.464)], b, 72, 3), chrome)
    return [m.build()]


def bouncy():
    """Swirled rubber super ball."""
    m = Model("piece_bouncy")
    b = y0()
    r = 0.088
    m.add(sphere((0, b + r, 0), r, segments=96, rings=48), P("swirl", "13A496", "F2C230"))
    return [m.build()]


def snowglobe():
    """Snow globe: turned walnut base with a brass plaque; cottage and fir tree in the snow."""
    m = Model("piece_snowglobe")
    b = y0()
    wood, gold, snow = P("varnish", "5A3420"), P("gold", "D4A84A"), P("frosting", "F5F7FA")
    m.add(lathe([(0.0, b), (0.086, b), (0.089, b + 0.004), (0.089, b + 0.01), (0.085, b + 0.014), (0.084, b + 0.02), (0.078, b + 0.036),
                 (0.07, b + 0.046), (0.068, b + 0.054), (0.064, b + 0.058), (0.0, b + 0.058)], segments=96), wood)
    m.add(torus((0, b + 0.017, 0), 0.0848, 0.0016, segments=96, ring_segments=6), gold)
    pc = V(0, b + 0.03, -0.0806)
    m.add(cbox(pc, (0.05, 0.015, 0.0016), bevel=0.0006), gold, transform=rotate_about(pc, "X", 20.6))
    c = V(0, b + 0.13, 0)
    m.add(sphere(c, 0.088, segments=64, rings=32), glass("E8F4FA"))
    m.add(lathe([(0.0, b + 0.06), (0.074, b + 0.06), (0.071, b + 0.068), (0.055, b + 0.08), (0.03, b + 0.086), (0.0, b + 0.088)], segments=64), snow)
    walls, roof = P("paint", "B8402F"), snow
    m.add(box((-0.034, b + 0.078, -0.012), (0.004, b + 0.106, 0.018), bevel=0.0015), walls)
    m.add(prism([(-0.039, 0.1045), (0.009, 0.1045), (-0.015, 0.126)], -0.0165, 0.0225, bevel=0.0015), roof, transform=Matrix.Translation((0, b, 0)))
    m.add(box((-0.008, b + 0.11, -0.002), (-0.002, b + 0.126, 0.004)), walls)
    m.add(box((-0.019, b + 0.08, -0.0128), (-0.011, b + 0.096, -0.0118)), P("varnish", "4A2A18"))
    for x in (-0.029, -0.004):
        m.add(box((x - 0.0035, b + 0.088, -0.0128), (x + 0.0035, b + 0.097, -0.0118)), glow("FFC86A"))
    tree = P("felt", "2E6B40")
    tp = V(0.034, b + 0.078, 0.004)
    m.add(cyl(tp + V(0, 0.006, 0), 0.003, 0.012, axis="y", segments=10), P("varnish", "4A2A18"))
    for k, (r, hgt, y) in enumerate(((0.019, 0.026, 0.01), (0.015, 0.022, 0.024), (0.01, 0.018, 0.037))):
        m.add(lathe([(0.0, 0.0), (r, 0.0), (r * 0.9, 0.003), (0.0, hgt)], segments=20), tree, transform=Matrix.Translation(tp + V(0, y, 0)))
        m.add(lathe([(0.0, hgt * 0.55), (r * 0.42, hgt * 0.55), (0.0, hgt * 1.01)], segments=20), snow, transform=Matrix.Translation(tp + V(0, y, 0)))
    rnd = random.Random(4)
    for k in range(18):
        a = rnd.uniform(0, 2 * math.pi)
        rr = rnd.uniform(0.01, 0.07)
        p = c + V(math.cos(a) * rr, rnd.uniform(-0.035, 0.07), math.sin(a) * rr * 0.8)
        m.add(sphere(p, 0.0022, segments=8, rings=5), P("matte", "FFFFFF"))
    return [m.build()]


def frog():
    """Tree frog: glossy mottled skin, pale throat, golden eyes, sticky toe pads."""
    m = Model("piece_frog")
    b = y0()
    skin, belly = P("skin", "4E8C2E"), P("flesh", "D8DFA2")
    turn = yaw(30)
    body = V(0, b + 0.062, 0.005)
    parts = [
        (ellipsoid(body, (0.072, 0.05, 0.07), segments=48, rings=28), skin),
        (ellipsoid(body + V(0, 0.012, -0.045), (0.062, 0.038, 0.045), segments=48, rings=28), skin),
        (ellipsoid(body + V(0, -0.018, -0.03), (0.058, 0.032, 0.05), segments=40, rings=24), belly),
        (ellipsoid(body + V(0, -0.006, -0.07), (0.04, 0.018, 0.02), segments=32, rings=16), belly),
    ]
    pads = []
    for s in (-1, 1):
        parts.append((sphere(body + V(s * 0.034, 0.042, -0.05), 0.02, segments=28, rings=18), skin))
        parts.append((ellipsoid(body + V(s * 0.062, -0.026, 0.03), (0.03, 0.022, 0.045), segments=32, rings=20), skin))
        shin = [body + V(s * 0.07, -0.04, 0.06), body + V(s * 0.088, -0.048, 0.02), body + V(s * 0.082, -0.054, -0.026)]
        parts.append((sweep(spline([tuple(p) for p in shin], 4), 0.014, radius_end=0.011, segments=18), skin))
        heel = shin[-1] + V(0, -0.002, 0)
        for t in (-0.6, -0.2, 0.2, 0.6):
            tip = heel + V(s * (0.02 + 0.016 * t), -0.006, -0.03 + abs(t) * 0.006)
            parts.append((sweep([heel, (heel + tip) / 2 + V(0, 0.002, 0), tip], 0.0045, radius_end=0.003, segments=10), skin))
            pads.append(tip)
        arm = [body + V(s * 0.04, -0.02, -0.06), body + V(s * 0.05, -0.04, -0.072), body + V(s * 0.05, -0.056, -0.08)]
        parts.append((sweep(spline([tuple(p) for p in arm], 4), 0.0105, radius_end=0.0085, segments=16), skin))
        hand = arm[-1]
        for t in (-0.5, 0.0, 0.5):
            tip = hand + V(s * 0.01 * t + s * 0.004, -0.002, -0.014 + abs(t) * 0.004)
            parts.append((sweep([hand, tip], 0.0035, radius_end=0.0028, segments=10), skin))
            pads.append(tip)
    for p in pads:
        parts.append((sphere(p, 0.0048, scale=(1, 0.75, 1), segments=12, rings=8), skin))
    m.add_fused(parts, voxel=0.0011, smooth_iters=5, smooth_factor=0.5, max_faces=42000, transform=turn)
    iris, pupil = P("bead", "C89A28"), P("bead", "0A0806")
    for s in (-1, 1):
        e = body + V(s * 0.036, 0.048, -0.056)
        d = V(s * 0.45, 0.25, -1).normalized()
        m.add(sphere(e, 0.0155, segments=28, rings=18), iris, transform=turn)
        m.add(ellipsoid((0, 0, 0), (0.0075, 0.0035, 0.0018), segments=16, rings=8), pupil, transform=turn @ frame_at(e + d * 0.0148, d))
        m.add(sphere(body + V(s * 0.009, 0.037, -0.0875), 0.0018, segments=8, rings=5), pupil, transform=turn)
    mouth = []
    for k in range(15):
        x = -0.042 + k * 0.006
        f = max(0.0, 1 - (x / 0.062) ** 2)
        mouth.append(body + V(x, 0.004 - 0.008 * f, -0.045 - 0.045 * math.sqrt(f) + 0.0004))
    m.add(sweep(mouth, 0.0011, segments=6), P("thread", "2C4020"), transform=turn)
    return [m.build()]


def _wing(root, s, membrane, bone):
    outline = [(0.0, 0.0), (-0.02, 0.03), (-0.11, 0.075), (-0.098, 0.05), (-0.118, 0.04), (-0.104, 0.028), (-0.14, 0.033), (-0.11, 0.014),
               (-0.06, 0.0)]
    shear = Matrix(((1, 0, 0, 0), (0, 0.92, 0, 0), (0, s * 0.18, 1, 0), (0, 0, 0, 1)))
    place = Matrix.Translation(V(root)) @ shear
    bm, sm = extrude_outline(outline, -0.0014, 0.0014, bevel=0.0006, segments=1)
    bm.transform(place)
    bm.normal_update()
    out = [((bm, sm), membrane)]
    wrist = (-0.02, 0.03)
    for p0, p1, r0 in (((0.0, 0.0), wrist, 0.0048), (wrist, (-0.11, 0.075), 0.004), (wrist, (-0.118, 0.04), 0.0024),
                       (wrist, (-0.14, 0.033), 0.0024), (wrist, (-0.11, 0.014), 0.0022)):
        a = place @ V(p0[0], p0[1], 0)
        c = place @ V(p1[0], p1[1], 0)
        out.append((sweep([a, (a + c) / 2, c], r0, radius_end=r0 * 0.45, segments=10), bone))
    return out


def dragon():
    """Young red dragon lying down: scaled hide, plated belly, folded membrane wings, ivory horns."""
    m = Model("piece_dragon")
    b = y0()
    scales, belly, membrane, ivory = P("scales", "B8352A"), P("armour", "E3A93A"), P("flesh", "7A2E55"), P("wax", "EFE3C6")
    body = V(-0.03, b + 0.075, 0.0)
    head = V(0.14, b + 0.12, -0.005)
    parts = [
        (ellipsoid(body, (0.11, 0.062, 0.068), segments=56, rings=32), scales),
        (ellipsoid(body + V(0.01, -0.022, -0.03), (0.09, 0.038, 0.045), segments=48, rings=24), belly),
        (sweep(bezier(body + V(0.06, 0.02, 0), body + V(0.12, 0.04, 0), head + V(-0.06, -0.01, 0), head + V(-0.01, 0, 0), 14), 0.04,
               radius_end=0.033, segments=28), scales),
        (ellipsoid(head, (0.05, 0.045, 0.045), segments=48, rings=28), scales),
        (ellipsoid(head + V(0.055, -0.016, 0), (0.045, 0.028, 0.034), segments=40, rings=24), scales),
        (ellipsoid(head + V(0.04, -0.032, -0.003), (0.042, 0.016, 0.03), segments=36, rings=20), belly),
    ]
    for s in (-1, 1):
        parts.append((ellipsoid(head + V(0.02, 0.026, s * 0.026), (0.022, 0.01, 0.012), segments=20, rings=12), scales))
        parts.append((sphere(head + V(0.096, -0.004, s * 0.014), 0.007, segments=14, rings=8), scales))
        fl = [body + V(0.06, -0.03, s * 0.045), body + V(0.07, -0.05, s * 0.05), body + V(0.076, -0.066, s * 0.052)]
        parts.append((sweep(spline([tuple(p) for p in fl], 4), 0.018, radius_end=0.014, segments=18), scales))
        parts.append((ellipsoid(body + V(0.086, -0.07, s * 0.052), (0.022, 0.01, 0.018), segments=20, rings=12), scales))
        parts.append((ellipsoid(body + V(-0.06, -0.025, s * 0.05), (0.035, 0.035, 0.022), segments=28, rings=18), scales))
        parts.append((ellipsoid(body + V(-0.04, -0.068, s * 0.055), (0.026, 0.01, 0.018), segments=20, rings=12), scales))
    tail = bezier(body + V(-0.1, -0.005, 0), body + V(-0.16, -0.03, 0), body + V(-0.2, 0.02, 0), body + V(-0.205, 0.07, -0.01), 20)
    parts.append((sweep(tail, 0.03, radius_end=0.008, segments=20), scales))
    m.add_fused(parts, voxel=0.0015, smooth_iters=5, smooth_factor=0.5, max_faces=46000)
    cone = [(0.0, 0.0), (0.0095, 0.0), (0.006, 0.01), (0.0, 0.024)]
    for k in range(7):
        x = -0.11 + k * 0.03
        f = max(0.0, 1 - ((x - body.x) / 0.11) ** 2)
        p = V(x, body.y + 0.062 * math.sqrt(f) - 0.004, 0)
        sc = 0.75 + 0.35 * math.sin(math.pi * k / 6)
        m.add(lathe([(r * sc, h * sc) for r, h in cone], segments=16), ivory, transform=Matrix.Translation(p) @ Matrix.Rotation(math.radians(18), 4, "Z"))
    for k in range(5, 18, 3):
        p, q = tail[k], tail[k + 1]
        d = (q - p).normalized()
        up = V(-d.y, d.x, 0).normalized()
        sc = 0.7 - k * 0.025
        m.add(lathe([(r * sc, h * sc) for r, h in cone], segments=12), ivory, transform=frame_at(p + up * (0.03 - k * 0.0012) * 0.85, d, up) @
              Matrix.Rotation(math.radians(-90), 4, "X"))
    m.add(prism([(-0.022, 0), (0.022, 0), (0, 0.034)], -0.0035, 0.0035, bevel=0.001), scales, transform=Matrix.Translation(tail[-1] + V(0, -0.004, 0)))
    for s in (-1, 1):
        m.add(sweep(bezier(head + V(-0.015, 0.035, s * 0.025), head + V(-0.035, 0.05, s * 0.03), head + V(-0.05, 0.062, s * 0.034),
                           head + V(-0.064, 0.07, s * 0.036), 12), 0.0085, radius_end=0.0012, segments=12), ivory)
        for p, mat in _wing(body + V(0.02, 0.048, s * 0.034), s, membrane, scales):
            m.add(p, mat)
        e = head + V(0.03, 0.018, s * 0.039)
        m.add(sphere(e, 0.0115, segments=24, rings=14), P("bead", "E8B020"))
        m.add(ellipsoid((0, 0, 0), (0.0016, 0.0075, 0.0016), segments=10, rings=8), P("bead", "0A0806"), transform=frame_at(e + V(0.004, 0, s * 0.0105), V(0.35, 0, s)))
        m.add(rod(head + V(0.072, -0.028, s * 0.02), head + V(0.074, -0.04, s * 0.021), 0.0026, radius2=0.0004, segments=8), ivory)
    return [m.build()]


def dragonegg():
    """Dragon egg: iridescent plated shell flecked with gold, in a twig nest."""
    m = Model("piece_dragonegg")
    b = y0()
    eggp = [(0.0, 0.0), (0.035, 0.004), (0.06, 0.03), (0.073, 0.075), (0.072, 0.11), (0.062, 0.148), (0.04, 0.178), (0.0, 0.192)]
    base = b + 0.014
    m.add(revolve(eggp, base, 96, 6), P("eggshell", "5E4290"))
    surf = [(r, base + h) for r, h in spline(eggp, 6)]
    rnd = random.Random(8)
    for k in range(46):
        a = k * 2.39996
        hh = base + 0.025 + (k * 0.618 % 1.0) * 0.15
        r = _radius_at(surf, hh)
        p = V(math.cos(a) * r, hh, math.sin(a) * r)
        sz = rnd.uniform(0.0022, 0.0042)
        m.add(ellipsoid((0, 0, 0), (sz, sz * rnd.uniform(0.6, 1.0), 0.0009), segments=10, rings=6), P("gold", "E0B54A"),
              transform=frame_at(p, V(math.cos(a), 0.1, math.sin(a))))
    m.add(torus((0, b + 0.016, 0), 0.058, 0.017, segments=48, ring_segments=12, scale=(1, 1, 0.75)), P("felt", "A08050"))
    twig = P("wood", "7A5A36")
    for k in range(48):
        a0 = rnd.uniform(0, 2 * math.pi)
        span = rnd.uniform(0.7, 1.4)
        rr = rnd.uniform(0.05, 0.074)
        yy = b + rnd.uniform(0.004, 0.03)
        pts = []
        for i in range(7):
            a = a0 + span * i / 6
            pts.append(V(math.cos(a) * (rr + rnd.uniform(-0.004, 0.004)), yy + rnd.uniform(-0.004, 0.004), math.sin(a) * (rr + rnd.uniform(-0.004, 0.004))))
        m.add(sweep(spline([tuple(p) for p in pts], 3), rnd.uniform(0.0024, 0.0036), radius_end=0.0012, segments=8), twig)
    return [m.build()]


# ============================================================================ padding

def crumpled(center, radius, seed, points=150, squash=(1.03, 0.97, 0.95)):
    """Crumpled paper ball: a jittered hull gives the big flat facets, then subdivided dents and a
    fine voronoi fold field add the smaller creases between them."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for _ in range(points):
        d = V(rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 1)).normalized()
        bm.verts.new(d * rnd.uniform(0.82, 1.06))
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges, cuts=5, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    off = V(seed * 17.3, seed * 5.1, seed * 11.7)
    for v in bm.verts:
        d = v.co.normalized()
        dent = min(0.0, noise.noise(d * 2.2 + off)) * 0.14
        dist, _ = noise.voronoi(d * 7.0 + off)
        fold = -0.035 * (1.0 - min(1.0, (dist[1] - dist[0]) * 6.0))
        v.co = v.co * (1 + dent + fold)
        v.co = V(v.co.x * squash[0], v.co.y * squash[1], v.co.z * squash[2]) * radius
    bm.transform(Matrix.Translation(V(center)))
    bm.normal_update()
    _sharpen(bm, 24)
    for f in bm.faces:
        f.smooth = True
    return bm, None


def paper():
    m = Model("piece_paper")
    b = y0()
    bm, sm = crumpled((0, 0, 0), 0.1, 1)
    lo = min(v.co.y for v in bm.verts)
    bm.transform(Matrix.Translation((0, b - lo, 0)))
    m.add((bm, sm), P("paper", "EEE6D3"))
    return [m.build()]


def _hex_points(u0, u1, v0, v1, step):
    out = []
    row = 0
    v = v0
    while v <= v1 + 1e-9:
        u = u0 + (step / 2 if row % 2 else 0)
        while u <= u1 + 1e-9:
            out.append((u, v))
            u += step
        v += step * 0.866
        row += 1
    return out


def bubble():
    """A small roll of bubble wrap seen end-on: spiral layers on the face, bubbles all round, taped."""
    m = Model("piece_bubble")
    b = y0()
    film = glass("D6EAF4")
    cy, R, hz = b + 0.102, 0.1, 0.094
    m.add(cyl((0, cy, 0), R - 0.006, 2 * hz, axis="z", segments=48), film)
    # spiral: concentric layers stepping out from the core, each ending in a small lip
    for k, r in enumerate((0.034, 0.047, 0.06, 0.073, 0.086)):
        m.add(torus((0, cy, -hz), r, 0.0035, axis="z", segments=48, ring_segments=6), film)
        a = k * 1.3
        m.add(sphere((math.cos(a) * (r + 0.006), cy + math.sin(a) * (r + 0.006), -hz - 0.001), 0.006, segments=10, rings=6), film)
    m.add(cyl((0, cy, 0), 0.024, 2 * hz + 0.004, axis="z", segments=28), P("cardboard", "B98A55"))     # core
    m.add(cyl((0, cy, 0), 0.018, 2 * hz + 0.006, axis="z", segments=24), P("matte", "4A3A2A"))
    # bubbles on the rolling surface
    rows, ring = 8, 30
    for i in range(rows):
        z = -hz + 0.012 + i * (2 * hz - 0.024) / (rows - 1)
        for j in range(ring):
            a = (j + 0.5 * (i % 2)) / ring * 2 * math.pi
            m.add(sphere((math.cos(a) * (R - 0.004), cy + math.sin(a) * (R - 0.004), z), 0.0085, segments=10, rings=7), film)
    # band of brown tape around the middle and a loose film tail
    m.add(cyl((0, cy, 0.02), R + 0.0065, 0.04, axis="z", segments=48), P("plastic", "C69A5E"))
    m.add(box((-0.1, b, -hz), (-0.04, b + 0.003, hz), bevel=0.001), film)
    return [m.build()]


def relief_block(x0, x1, ya, yb, z_back, z_front, depth, cells, res=72):
    """Block whose front (-z) face is convoluted egg-crate foam."""
    bm = bmesh.new()

    def zf(i, j):
        u, v = i / res * cells, j / res * cells
        e = (math.cos(2 * math.pi * u) * math.cos(2 * math.pi * v) + 1) / 2
        return z_front + depth * (1 - e)

    xs = [x0 + (x1 - x0) * i / res for i in range(res + 1)]
    ys = [ya + (yb - ya) * j / res for j in range(res + 1)]
    front = [[bm.verts.new((xs[i], ys[j], zf(i, j))) for j in range(res + 1)] for i in range(res + 1)]
    for i in range(res):
        for j in range(res):
            bm.faces.new((front[i][j], front[i][j + 1], front[i + 1][j + 1], front[i + 1][j]))
    ring = ([front[i][0] for i in range(res + 1)] + [front[res][j] for j in range(1, res + 1)] +
            [front[i][res] for i in range(res - 1, -1, -1)] + [front[0][j] for j in range(res - 1, 0, -1)])
    back = [bm.verts.new((v.co.x, v.co.y, z_back)) for v in ring]
    n = len(ring)
    for k in range(n):
        bm.faces.new((ring[k], back[k], back[(k + 1) % n], ring[(k + 1) % n]))
    bm.faces.new(list(reversed(back)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    _sharpen(bm, 40)
    for f in bm.faces:
        f.smooth = True
    return bm, None


def foam():
    m = Model("piece_foam")
    b = y0()
    m.add(relief_block(-0.112, 0.112, b, b + 0.226, 0.11, -0.11, 0.032, 4), P("foam", "A9BAC4"))
    return [m.build()]


def shards():
    """Generic pile of broken pieces; material slot recoloured per item in Unity."""
    m = Model("piece_shards")
    b = y0()
    rnd = random.Random(5)
    for k in range(11):
        n = rnd.randint(5, 8)
        size = rnd.uniform(0.022, 0.048)
        outline = []
        for i in range(n):
            a = 2 * math.pi * i / n + rnd.uniform(-0.25, 0.25)
            rr = size * rnd.uniform(0.5, 1.0)
            outline.append((math.cos(a) * rr, math.sin(a) * rr))
        bm, sm = extrude_outline(outline, -0.0024, 0.0024, bevel=0.0007, segments=1)
        x, z = rnd.uniform(-0.08, 0.08), rnd.uniform(-0.06, 0.06)
        tilt = rnd.uniform(-35, 35)
        lift = abs(math.sin(math.radians(tilt))) * size * 0.6
        M = (Matrix.Translation((x, b + 0.0025 + lift + rnd.uniform(0, 0.012), z)) @ Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Y") @
             Matrix.Rotation(math.radians(90 + tilt), 4, "X"))
        m.add((bm, sm), shiny("FFFFFF"), transform=M)
    return [m.build()]


def puddle():
    m = Model("piece_puddle")
    b = y0()
    bm, sm = sphere((0, 0, 0), 1.0, scale=(1, 1, 1), segments=96, rings=24)
    for v in bm.verts:
        a = math.atan2(v.co.z, v.co.x)
        r = 0.1 + 0.022 * math.sin(a * 3 + 0.4) + 0.012 * math.cos(a * 5) + 0.006 * math.sin(a * 9)
        v.co = V(v.co.x * r, max(v.co.y, -0.2) * 0.005, v.co.z * r * 0.8)
    bm.transform(Matrix.Translation((0, b + 0.001, 0)))
    bm.normal_update()
    m.add((bm, sm), glass("FFFFFF"))
    return [m.build()]


# ============================================================================ materials (statics)

def divider():
    m = Model("divider")
    # unit height (scaled to the box interior in Unity), centred on the origin
    m.add(box((-0.0075, -0.5, -0.152), (0.0075, 0.5, 0.152), bevel=0.002), tex("kraft"), uv_scale=2.0)
    m.add(box((-0.0076, -0.5, -0.153), (0.0076, 0.5, -0.151)), tex("corrugate"), uv_scale=8.0)
    return [m.build()]


def shelf():
    m = Model("shelf")
    m.add(box((-0.5, -0.0075, -0.152), (0.5, 0.0075, 0.152), bevel=0.002), tex("kraft"), uv_scale=2.0)
    m.add(box((-0.5, -0.0076, -0.153), (0.5, 0.0076, -0.151)), tex("corrugate"), uv_scale=8.0)
    return [m.build()]


def strap():
    """Woven nylon strap with a cam buckle (stretched to the item width in Unity)."""
    m = Model("strap")
    web, steel = P("webbing", "2E4A7A"), P("brushed", "C9CDD2")
    w = 0.125  # half width of one cell
    m.add(box((-w - 0.004, -0.018, -0.128), (w + 0.004, 0.018, -0.12), bevel=0.0008), web)
    for s in (-1, 1):
        m.add(box((s * (w + 0.004) - 0.004, -0.018, -0.128), (s * (w + 0.004) + 0.004, 0.018, 0.13), bevel=0.0008), web)
    frame = boolean(box((-0.024, -0.027, -0.135), (0.024, 0.027, -0.127), bevel=0.003, segments=3),
                    [box((-0.017, -0.02, -0.14), (0.017, 0.02, -0.12))])
    m.add(frame, steel, smooth="auto")
    m.add(rod((-0.017, 0, -0.131), (0.017, 0, -0.131), 0.0022, segments=12), steel)
    m.add(box((-0.014, -0.0185, -0.1305), (0.014, 0.0185, -0.1285), bevel=0.0006), web)
    return [m.build()]


ALL = {
    "teacup": teacup, "books": books, "teddy": teddy, "vase": vase, "bowling": bowling, "armadillo": armadillo,
    "armadillo_awake": armadillo_awake, "magnet": magnet, "potion": potion, "cake": cake, "balloon": balloon,
    "cactus": cactus, "robot": robot, "iceswan": iceswan, "lavalamp": lavalamp, "bouncy": bouncy,
    "snowglobe": snowglobe, "frog": frog, "dragon": dragon, "dragonegg": dragonegg,
    "paper": paper, "bubble": bubble, "foam": foam, "shards": shards, "puddle": puddle,
    "divider": divider, "shelf": shelf, "strap": strap,
}
