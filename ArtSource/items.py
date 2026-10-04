"""Item, padding and packing-material models. One builder per piece; each returns root objects.

Conventions (Unity space, metres): a piece of W x H cells is centred on the origin and fills
x in [-W*0.125, W*0.125], y in [-H*0.125, H*0.125]; the sim insets bodies by 0.0075 vertically,
so models rest on y0 = -H*0.125 + 0.0075. Depth stays within z in [-0.12, 0.12]; the camera
looks along +z, so faces pointing -z are the "front". Creatures face +x (right) by default.
"""
import math

from mathutils import Matrix, Vector

from hwc_lib import (CELL, Model, V, box, bezier, bumps_on_face, cbox, col, cyl, displace, ellipsoid, empty,
                     extrude_outline, glass, glow, hull, ico, lathe, matte, metal, prism, rod, rotate_about,
                     shade, shell, shiny, sphere, sweep, tex, torus, transformed)


def y0(h=1):
    return -h * CELL / 2 + 0.0075


def yaw(deg, center=(0, 0, 0)):
    return rotate_about(center, "Y", deg)


# ============================================================================ items

def teacup():
    m = Model("piece_teacup")
    b = y0()
    white, gold, blue = shiny("F7F3EC"), metal("E0B54A"), shiny("4F7FC0")
    # saucer
    m.add(lathe([(0.0, b), (0.07, b), (0.1, b + 0.008), (0.112, b + 0.016), (0.104, b + 0.019), (0.05, b + 0.013), (0.0, b + 0.013)]), white)
    m.add(torus((0, b + 0.0165, 0), 0.109, 0.0032, axis="y"), gold)
    # cup body (hollow)
    prof = [(0.042, b + 0.014), (0.052, b + 0.02), (0.07, b + 0.05), (0.083, b + 0.1), (0.09, b + 0.15), (0.093, b + 0.172)]
    m.add(shell(prof, thickness=0.007), white)
    m.add(lathe([(0.0, b + 0.012), (0.044, b + 0.012), (0.046, b + 0.02), (0.0, b + 0.02)]), white)
    m.add(torus((0, b + 0.172, 0), 0.09, 0.0045, axis="y"), gold)
    m.add(torus((0, b + 0.105, 0), 0.0845, 0.006, axis="y"), blue)
    for k in range(6):  # little blue flowers around the band
        a = k / 6 * 2 * math.pi + 0.3
        p = V(math.cos(a) * 0.081, b + 0.138, math.sin(a) * 0.081)
        m.add(sphere(p, 0.008, scale=(1, 1, 0.5), segments=10, rings=6), blue,
              transform=Matrix.Translation(p) @ V(0, 0, 1).rotation_difference(V(p.x, 0, p.z).normalized()).to_matrix().to_4x4() @ Matrix.Translation(-p))
    # tea
    m.add(cyl((0, b + 0.155, 0), 0.083, 0.004, axis="y", segments=28), col("8A4E24"))
    # handle (C shape on the right)
    m.add(torus((0.098, b + 0.1, 0), 0.034, 0.0095, axis="z", arc=0.62, start=-0.31, ring_segments=10), white)
    return [m.build()]


def books():
    m = Model("piece_books")
    b = y0()
    page = matte("F6EFDD")
    specs = [  # (width, height, depth, x offset, cover colour, spine toward camera?)
        (0.47, 0.074, 0.2, 0.0, "B8423A", True),
        (0.43, 0.07, 0.19, 0.012, "2E4A7A", False),
        (0.45, 0.068, 0.185, -0.01, "3E7A4F", True),
    ]
    y = b
    for w, h, d, xo, c, spine_front in specs:
        cover = shiny(c)
        t = 0.007
        x0, x1 = xo - w / 2, xo + w / 2
        z0, z1 = -d / 2, d / 2
        m.add(box((x0, y, z0), (x1, y + t, z1), bevel=0.003), cover)
        m.add(box((x0, y + h - t, z0), (x1, y + h, z1), bevel=0.003), cover)
        zs = z0 if spine_front else z1 - t * 1.4
        m.add(box((x0, y + t * 0.5, zs), (x1, y + h - t * 0.5, zs + t * 1.4), bevel=0.004), cover)
        pz0, pz1 = (z0 + t * 1.2, z1 - 0.004) if spine_front else (z0 + 0.004, z1 - t * 1.2)
        m.add(box((x0 + 0.006, y + t, pz0), (x1 - 0.006, y + h - t, pz1), bevel=0.002), page)
        if spine_front:
            for bx in (x0 + 0.05, x1 - 0.05):
                m.add(box((bx - 0.006, y + 0.004, zs - 0.0015), (bx + 0.006, y + h - 0.004, zs + 0.004)), metal("E0B54A"))
            m.add(box((xo - 0.07, y + h * 0.3, zs - 0.0015), (xo + 0.07, y + h * 0.7, zs + 0.002), bevel=0.002), col(shade(c, 0.6)))
        y += h + 0.002
    return [m.build()]


def teddy():
    m = Model("piece_teddy")
    b = y0()
    fur, light, dark = col("B27A4A"), col("E3B784"), shiny("2A1C14")
    m.add(ellipsoid((0, b + 0.07, 0.01), (0.068, 0.072, 0.058)), fur)
    m.add(ellipsoid((0, b + 0.06, -0.035), (0.042, 0.045, 0.02)), light)
    m.add(sphere((0, b + 0.165, 0), 0.062), fur)
    for s in (-1, 1):
        m.add(sphere((s * 0.048, b + 0.215, 0.004), 0.024, scale=(1, 1, 0.6)), fur)
        m.add(sphere((s * 0.048, b + 0.215, -0.006), 0.014, scale=(1, 1, 0.5)), light)
        m.add(ellipsoid((s * 0.07, b + 0.085, -0.01), (0.022, 0.04, 0.024)), fur, transform=rotate_about((s * 0.07, b + 0.085, 0), "Z", s * -25))
        m.add(ellipsoid((s * 0.042, b + 0.022, -0.04), (0.03, 0.024, 0.038)), fur)
        m.add(ellipsoid((s * 0.042, b + 0.022, -0.077), (0.019, 0.017, 0.006)), light)
        m.add(sphere((s * 0.024, b + 0.18, -0.052), 0.0085), dark)
    m.add(ellipsoid((0, b + 0.145, -0.05), (0.03, 0.022, 0.018)), light)
    m.add(ellipsoid((0, b + 0.155, -0.067), (0.011, 0.008, 0.006)), dark)
    # bow tie
    bow = shiny("D9483B")
    m.add(hull([(0, b + 0.118, -0.06), (-0.03, b + 0.13, -0.055), (-0.03, b + 0.1, -0.055), (0, b + 0.112, -0.05)], bevel=0.003), bow)
    m.add(hull([(0, b + 0.118, -0.06), (0.03, b + 0.13, -0.055), (0.03, b + 0.1, -0.055), (0, b + 0.112, -0.05)], bevel=0.003), bow)
    m.add(sphere((0, b + 0.115, -0.062), 0.008), bow)
    return [m.build()]


def vase():
    m = Model("piece_vase")
    b = y0(2)
    glaze, white, gold = shiny("3D8FA8"), shiny("F4EFE4"), metal("E0B54A")
    prof = [(0.0, b), (0.05, b), (0.058, b + 0.012), (0.09, b + 0.07), (0.104, b + 0.15), (0.095, b + 0.22),
            (0.06, b + 0.3), (0.045, b + 0.36), (0.05, b + 0.41), (0.068, b + 0.455), (0.07, b + 0.468), (0.0, b + 0.468)]
    m.add(lathe(prof, segments=36), glaze)
    m.add(cyl((0, b + 0.469, 0), 0.055, 0.003, axis="y"), col("173840"))
    m.add(torus((0, b + 0.462, 0), 0.068, 0.006), gold)
    m.add(torus((0, b + 0.15, 0), 0.104, 0.007, segments=40), white)
    m.add(torus((0, b + 0.11, 0), 0.098, 0.004, segments=40), white)
    m.add(torus((0, b + 0.19, 0), 0.101, 0.004, segments=40), white)
    m.add(torus((0, b + 0.012, 0), 0.056, 0.005), gold)
    # leaf motifs on the belly
    for k in range(8):
        a = k / 8 * 2 * math.pi
        r = 0.097
        p = V(math.cos(a) * r, b + 0.235, math.sin(a) * r)
        m.add(ellipsoid(p, (0.012, 0.03, 0.006), segments=10, rings=6), white,
              transform=Matrix.Translation(p) @ V(0, 0, 1).rotation_difference(V(p.x, 0, p.z).normalized()).to_matrix().to_4x4() @ Matrix.Translation(-p))
    return [m.build()]


def bowling():
    m = Model("piece_bowling")
    b = y0()
    r = 0.112
    c = V(0, b + r, 0)
    m.add(sphere(c, r, segments=36, rings=20), shiny("4B2E6E"))
    m.add(torus(c, r * 0.985, 0.006, axis="x", segments=40, start=0.0, arc=0.35), shiny("7E5BB0"), transform=rotate_about(c, "Y", 30))
    for dx, dy in ((-0.028, 0.04), (0.028, 0.04), (0.0, -0.005)):
        d = V(dx, dy, -1).normalized()
        p = c + d * (r - 0.004)
        m.add(rod(p - d * 0.01, p + d * 0.004, 0.014, segments=14), col("15101C"))
    return [m.build()]


def _armadillo_curled(name, eyes_open=False):
    m = Model(name)
    b = y0()
    shell_c, band_c, skin = col("9E8F7A"), col("7D6F5C"), col("E2B8A5")
    r = 0.104
    c = V(0, b + r, 0)
    m.add(sphere(c, r, scale=(1.0, 1.0, 0.92), segments=32, rings=18), shell_c)
    for k in range(-3, 4):  # bands wrapping around the ball (around the z axis = front-back)
        a = k * 0.36
        rr = r * math.cos(a) * 1.01
        cc = c + V(math.sin(a) * r * 1.01, 0, 0)
        if rr < 0.02:
            continue
        m.add(torus(cc, rr, 0.008, axis="x", segments=32, ring_segments=8, scale=(1, 1, 0.92)), band_c)
    # tucked head on the right
    head = c + V(0.075, -0.045, -0.03)
    m.add(ellipsoid(head, (0.045, 0.038, 0.04)), skin)
    m.add(ellipsoid(head + V(0.04, -0.012, -0.012), (0.022, 0.014, 0.014)), skin)
    m.add(sphere(head + V(0.06, -0.014, -0.014), 0.006), shiny("3A2420"))
    for s in (-1, 1):
        m.add(ellipsoid(head + V(-0.01, 0.034, s * 0.022), (0.014, 0.022, 0.006)), skin,
              transform=rotate_about(head + V(-0.01, 0.034, s * 0.022), "Z", -25))
    if eyes_open:
        m.add(sphere(head + V(0.022, 0.012, -0.035), 0.008), shiny("15100E"))
    else:
        m.add(torus(head + V(0.022, 0.01, -0.036), 0.008, 0.0018, axis="z", arc=0.5, start=0.5, segments=10, ring_segments=6), col("3A2420"))
    # tail tip
    m.add(sweep(bezier(c + V(-0.07, -0.07, -0.03), c + V(-0.1, -0.09, -0.04), c + V(-0.09, -0.11, -0.05), c + V(-0.05, -0.1, -0.06), 10), 0.012, radius_end=0.004), band_c)
    return m


def armadillo():
    return [_armadillo_curled("piece_armadillo").build()]


def armadillo_awake():
    """Uncurled and grumpy (shown in the reveal when it woke up)."""
    m = Model("piece_armadillo_awake")
    b = y0()
    shell_c, band_c, skin = col("9E8F7A"), col("7D6F5C"), col("E2B8A5")
    body = V(-0.01, b + 0.075, 0)
    m.add(ellipsoid(body, (0.085, 0.06, 0.06)), shell_c)
    for k in range(-3, 4):
        x = body.x + k * 0.022
        rr = 0.06 * math.sqrt(max(0.05, 1 - (k * 0.022 / 0.085) ** 2)) * 1.03
        m.add(torus(V(x, body.y, 0), rr, 0.007, axis="x", segments=28, ring_segments=8, arc=0.5, start=0.0), band_c)
    head = body + V(0.085, 0.02, 0)
    m.add(ellipsoid(head, (0.035, 0.03, 0.03)), skin)
    m.add(rod(head + V(0.02, -0.005, 0), head + V(0.07, -0.015, 0), 0.012, radius2=0.006), skin)
    for s in (-1, 1):
        m.add(ellipsoid(head + V(-0.01, 0.035, s * 0.02), (0.012, 0.025, 0.005)), skin)
        m.add(ellipsoid((body.x + 0.05 * s, b + 0.02, -0.03), (0.012, 0.022, 0.012)), skin)
        m.add(ellipsoid((body.x + 0.05 * s, b + 0.02, 0.03), (0.012, 0.022, 0.012)), skin)
    m.add(sphere(head + V(0.015, 0.012, -0.027), 0.007), shiny("15100E"))
    m.add(rod(head + V(0.006, 0.026, -0.03), head + V(0.03, 0.02, -0.03), 0.002), col("3A2420"))  # grumpy brow
    m.add(sweep(bezier(body + V(-0.08, -0.01, 0), body + V(-0.12, -0.02, 0), body + V(-0.12, -0.05, 0), body + V(-0.105, -0.06, 0), 10), 0.012, radius_end=0.003), band_c)
    return [m.build()]


def magnet():
    m = Model("piece_magnet")
    b = y0()
    red, silver = shiny("D9483B"), metal("D7DCE0")
    cy = b + 0.13
    R, r = 0.062, 0.031
    m.add(torus((0, cy, 0), R, r, axis="z", arc=0.5, start=0.0, segments=24, ring_segments=12), red)
    for s in (-1, 1):
        m.add(cyl((s * R, cy - 0.04, 0), r, 0.08, axis="y", segments=20), red)
        m.add(cyl((s * R, b + 0.024, 0), r * 1.02, 0.034, axis="y", segments=20, bevel=0.004), silver)
    m.add(box((-0.02, cy + R - 0.012, -r - 0.002), (0.02, cy + R + 0.012, -r + 0.004), bevel=0.002), col("F6EFDD"))
    return [m.build()]


def potion():
    m = Model("piece_potion")
    b = y0(2)
    gl, liquid, cork = glass("D9C8F0"), glow("8E4FC4"), col("A9784E")
    m.add(lathe([(0.0, b), (0.05, b + 0.004), (0.085, b + 0.03), (0.103, b + 0.09), (0.095, b + 0.15), (0.06, b + 0.19),
                 (0.034, b + 0.215), (0.03, b + 0.36), (0.04, b + 0.372), (0.04, b + 0.38), (0.0, b + 0.38)], segments=36), gl)
    m.add(lathe([(0.0, b + 0.012), (0.05, b + 0.014), (0.08, b + 0.035), (0.093, b + 0.085), (0.088, b + 0.13), (0.0, b + 0.13)], segments=32), liquid)
    for k, (x, yy, rr) in enumerate(((0.02, 0.15, 0.01), (-0.015, 0.175, 0.007), (0.005, 0.24, 0.006), (-0.006, 0.29, 0.005))):
        m.add(sphere((x, b + yy, -0.01), rr, segments=10, rings=6), glow("C9A5F2"))
    m.add(lathe([(0.0, b + 0.37), (0.028, b + 0.37), (0.034, b + 0.4), (0.036, b + 0.43), (0.0, b + 0.43)], segments=20), cork)
    # tag on a string
    m.add(sweep([V(0.03, b + 0.34, -0.02), V(0.055, b + 0.31, -0.03), V(0.07, b + 0.28, -0.035)], 0.0018, segments=6), col("8A6A4A"))
    m.add(box((0.055, b + 0.235, -0.04), (0.095, b + 0.282, -0.036), bevel=0.002), matte("F3E9D2"), transform=rotate_about((0.075, b + 0.26, -0.038), "Z", -12))
    return [m.build()]


def cake():
    m = Model("piece_cake")
    b = y0()
    plate, sponge, frost, pink2 = shiny("F7F3EC"), col("E9C08C"), shiny("F4A7B9"), shiny("E57F9A")
    m.add(lathe([(0.0, b), (0.19, b), (0.225, b + 0.01), (0.235, b + 0.016), (0.0, b + 0.012)], segments=40), plate)
    m.add(cyl((0, b + 0.07, 0), 0.19, 0.11, axis="y", segments=40, bevel=0.01), sponge)
    m.add(cyl((0, b + 0.128, 0), 0.196, 0.026, axis="y", segments=40, bevel=0.012), frost)
    for k in range(14):  # frosting drips
        a = k / 14 * 2 * math.pi + 0.1
        ln = 0.025 + 0.02 * ((k * 7) % 3) / 2
        p = V(math.cos(a) * 0.192, b + 0.115, math.sin(a) * 0.192)
        m.add(rod(p, p - V(0, ln, 0), 0.011, segments=10, radius2=0.009), frost)
        m.add(sphere(p - V(0, ln, 0), 0.009, segments=10, rings=6), frost)
    for k in range(10):
        a = k / 10 * 2 * math.pi
        m.add(sphere((math.cos(a) * 0.165, b + 0.143, math.sin(a) * 0.165), 0.013, segments=10, rings=6), pink2)
    for x in (-0.08, 0.0, 0.08):
        m.add(cyl((x, b + 0.175, 0.02 if x else -0.01), 0.008, 0.07, axis="y", segments=12), shiny("6FB4E8" if x < 0 else ("F2D24A" if x == 0 else "8ED17A")))
        m.add(ellipsoid((x, b + 0.222, 0.02 if x else -0.01), (0.008, 0.014, 0.008), segments=10, rings=6), glow("FFC24A"))
    m.add(sphere((0.04, b + 0.156, -0.08), 0.016), shiny("C4202E"))
    return [m.build()]


def balloon():
    m = Model("piece_balloon")
    b = y0()
    red = shiny("E8443A")
    c = V(0, b + 0.135, 0)
    m.add(lathe([(0.0, -0.105), (0.03, -0.098), (0.065, -0.07), (0.088, -0.02), (0.09, 0.03), (0.075, 0.075), (0.045, 0.1), (0.0, 0.108)], segments=32), red,
          transform=Matrix.Translation(c))
    m.add(lathe([(0.0, -0.002), (0.012, 0.0), (0.006, 0.012), (0.0, 0.014)], segments=12), red, transform=Matrix.Translation(c + V(0, -0.118, 0)))
    pts = [c + V(0, -0.118, 0)]
    for k in range(1, 12):
        t = k / 11
        pts.append(c + V(0.012 * math.sin(t * 9), -0.118 - t * 0.115, -0.01 * math.cos(t * 9)))
    m.add(sweep(pts, 0.0016, segments=6), col("F3E9D2"))
    m.add(ellipsoid(c + V(-0.035, 0.04, -0.07), (0.012, 0.022, 0.006)), glow("FFE0D8"))  # highlight
    return [m.build()]


def cactus():
    m = Model("piece_cactus")
    b = y0()
    pot, rim, soil = col("C8693F"), col("B05A34"), col("4A3424")
    m.add(lathe([(0.0, b), (0.06, b), (0.072, b + 0.07), (0.0, b + 0.07)], segments=28), pot)
    m.add(lathe([(0.07, b + 0.062), (0.082, b + 0.062), (0.082, b + 0.09), (0.0, b + 0.09), (0.0, b + 0.062)], segments=28), rim)
    m.add(cyl((0, b + 0.088, 0), 0.074, 0.006, axis="y"), soil)
    green, spine = col("5E9E4A"), matte("F6F0D8")
    body = []
    for k in range(13):
        t = k / 12
        rr = 0.048 * math.sin(math.pi * (0.08 + 0.92 * t)) ** 0.5 if t < 1 else 0.0
        body.append((max(rr, 0.0), b + 0.085 + t * 0.14))
    prim = lathe(body, segments=24)
    bm, sm = prim
    for v in bm.verts:  # vertical ribs
        a = math.atan2(v.co.z, v.co.x)
        rad = math.hypot(v.co.x, v.co.z)
        k = 1 + 0.09 * math.cos(a * 8)
        v.co.x *= k
        v.co.z *= k
    m.add((bm, sm), green)
    for s, h, ln in ((-1, 0.14, 0.05), (1, 0.165, 0.04)):
        base = V(s * 0.04, b + h, 0)
        pts = bezier(base, base + V(s * 0.035, 0, 0), base + V(s * 0.05, 0.01, 0), base + V(s * 0.05, ln, 0), 8)
        m.add(sweep(pts, 0.018, radius_end=0.015, segments=12), green)
        m.add(sphere(pts[-1], 0.015, segments=12, rings=8), green)
    for k in range(40):  # spines
        a = (k * 2.39996) % (2 * math.pi)
        hh = b + 0.1 + (k % 10) * 0.012
        rr = 0.047 * math.sin(math.pi * (0.08 + 0.92 * (hh - b - 0.085) / 0.14)) ** 0.5 + 0.004
        p = V(math.cos(a) * rr, hh, math.sin(a) * rr)
        d = V(p.x, 0, p.z).normalized()
        m.add(rod(p, p + d * 0.012, 0.0022, segments=5, radius2=0.0003), spine)
    for k in range(5):
        a = k / 5 * 2 * math.pi
        m.add(ellipsoid((math.cos(a) * 0.012, b + 0.228, math.sin(a) * 0.012), (0.012, 0.006, 0.012), segments=10, rings=6), shiny("F07AA8"))
    m.add(sphere((0, b + 0.232, 0), 0.007), shiny("F7D24A"))
    return [m.build()]


def robot():
    m = Model("piece_robot")
    b = y0()
    tin, red, dark = metal("A9B4BE"), shiny("D9483B"), col("3A4048")
    turn = yaw(32, (0, 0, 0))
    m.add(box((-0.05, b + 0.035, -0.045), (0.05, b + 0.125, 0.045), bevel=0.012), tin, transform=turn)
    m.add(box((-0.03, b + 0.06, -0.047), (0.03, b + 0.1, -0.044), bevel=0.003), red, transform=turn)
    for k in range(3):
        m.add(sphere((-0.018 + k * 0.018, b + 0.08, -0.049), 0.005, segments=8, rings=6), glow("F2D24A"), transform=turn)
    m.add(box((-0.04, b + 0.13, -0.038), (0.04, b + 0.2, 0.038), bevel=0.012), tin, transform=turn)
    for s in (-1, 1):
        m.add(cyl((s * 0.017, b + 0.17, -0.039), 0.012, 0.006, axis="z", segments=16), glow("FFD86A"), transform=turn)
        m.add(cyl((s * 0.017, b + 0.17, -0.043), 0.005, 0.003, axis="z", segments=10), dark, transform=turn)
        m.add(box((s * 0.03 - 0.012, b, -0.025), (s * 0.03 + 0.012, b + 0.04, 0.02), bevel=0.005), dark, transform=turn)
        m.add(rod((s * 0.055, b + 0.115, 0), (s * 0.075, b + 0.07, -0.01), 0.009, segments=10), tin, transform=turn)
        m.add(torus((s * 0.078, b + 0.062, -0.012), 0.012, 0.004, axis="z", arc=0.7, start=0.65, segments=12, ring_segments=6), dark, transform=turn)
    m.add(box((-0.02, b + 0.142, -0.041), (0.02, b + 0.15, -0.038)), dark, transform=turn)
    m.add(rod((0, b + 0.2, 0), (0, b + 0.226, 0), 0.003), dark, transform=turn)
    m.add(sphere((0, b + 0.229, 0), 0.008), glow("FF6A4A"), transform=turn)
    # wind-up key on the back
    m.add(rod((0, b + 0.09, 0.045), (0, b + 0.09, 0.07), 0.005), metal("E0B54A"), transform=turn)
    m.add(torus((0, b + 0.1, 0.075), 0.016, 0.005, axis="z", segments=16, ring_segments=6, scale=(1, 0.7, 1)), metal("E0B54A"), transform=turn)
    return [m.build()]


def iceswan():
    m = Model("piece_iceswan")
    b = y0(2)
    ice, deep = glass("BFE6F2"), glass("9ED3E8")
    m.add(box((-0.1, b, -0.075), (0.1, b + 0.035, 0.075), bevel=0.012), deep)
    body = V(-0.015, b + 0.1, 0)
    m.add(ellipsoid(body, (0.085, 0.06, 0.065)), ice)
    for s in (-1, 1):
        w = body + V(-0.02, 0.03, s * 0.05)
        m.add(ellipsoid(w, (0.07, 0.035, 0.014)), ice, transform=rotate_about(w, "Z", 22))
    neck = bezier(body + V(0.06, 0.02, 0), body + V(0.11, 0.08, 0), body + V(0.0, 0.17, 0), body + V(0.06, 0.29, 0), 16)
    m.add(sweep(neck, 0.022, radius_end=0.016, segments=14), ice)
    head = neck[-1] + V(0.01, 0.012, 0)
    m.add(sphere(head, 0.026), ice)
    m.add(rod(head + V(0.018, -0.004, 0), head + V(0.06, -0.012, 0), 0.01, radius2=0.002, segments=10), deep)
    m.add(sweep(bezier(body + V(-0.08, 0.0, 0), body + V(-0.11, 0.02, 0), body + V(-0.11, 0.05, 0), body + V(-0.09, 0.06, 0), 8), 0.014, radius_end=0.004), ice)
    return [m.build()]


def lavalamp():
    m = Model("piece_lavalamp")
    b = y0(2)
    chrome, gl, lava = metal("C9CED4"), glass("FFC27A"), glow("F28A2E")
    m.add(lathe([(0.0, b), (0.075, b), (0.074, b + 0.012), (0.045, b + 0.11), (0.0, b + 0.11)], segments=32), chrome)
    m.add(lathe([(0.0, b + 0.105), (0.045, b + 0.105), (0.062, b + 0.2), (0.05, b + 0.3), (0.034, b + 0.385), (0.0, b + 0.385)], segments=32), gl)
    for x, yy, r in ((0.0, 0.13, 0.04), (0.012, 0.2, 0.022), (-0.015, 0.26, 0.016), (0.008, 0.33, 0.012), (-0.02, 0.165, 0.014)):
        m.add(sphere((x, b + yy, -0.004), r, scale=(1, 1.25, 1), segments=14, rings=10), lava)
    m.add(lathe([(0.0, b + 0.38), (0.036, b + 0.38), (0.024, b + 0.46), (0.0, b + 0.46)], segments=24), chrome)
    return [m.build()]


def bouncy():
    m = Model("piece_bouncy")
    b = y0()
    r = 0.088
    c = V(0, b + r, 0)
    m.add(sphere(c, r, segments=32, rings=18), shiny("2FC4B2"))
    m.add(torus(c, r * 1.0, 0.012, axis="z", segments=36, ring_segments=8), shiny("F7D24A"), transform=rotate_about(c, "X", 25))
    m.add(torus(c, r * 1.0, 0.009, axis="x", segments=36, ring_segments=8), shiny("F07AA8"), transform=rotate_about(c, "Y", 40))
    return [m.build()]


def snowglobe():
    m = Model("piece_snowglobe")
    b = y0()
    wood, gl = shiny("6E4528"), glass("E8F4FA")
    m.add(lathe([(0.0, b), (0.082, b), (0.088, b + 0.012), (0.075, b + 0.048), (0.064, b + 0.056), (0.0, b + 0.056)], segments=32), wood)
    m.add(torus((0, b + 0.03, 0), 0.084, 0.004), metal("E0B54A"))
    c = V(0, b + 0.13, 0)
    m.add(sphere(c, 0.088, segments=32, rings=18), gl)
    m.add(ellipsoid(c + V(0, -0.055, 0), (0.07, 0.022, 0.07)), matte("FFFFFF"))
    m.add(box(c + V(-0.03, -0.045, -0.015), c + V(0.012, -0.012, 0.02), bevel=0.003), col("D9483B"))
    m.add(prism([(-0.036, -0.012), (0.018, -0.012), (-0.009, 0.014)], -0.019, 0.024), col("6E4528"), transform=Matrix.Translation(c))
    m.add(lathe([(0.0, 0), (0.02, 0), (0.0, 0.06)], segments=10), col("3E7A4F"), transform=Matrix.Translation(c + V(0.03, -0.045, 0)))
    for k in range(12):
        a = k * 2.4
        p = c + V(math.cos(a) * 0.05 * ((k % 3) / 3 + 0.3), -0.02 + (k % 5) * 0.018, math.sin(a) * 0.04)
        m.add(sphere(p, 0.004, segments=6, rings=4), matte("FFFFFF"))
    return [m.build()]


def frog():
    m = Model("piece_frog")
    b = y0()
    g, belly, dark = col("6DBF4A"), col("C9E59A"), shiny("15100E")
    turn = yaw(30)
    body = V(0, b + 0.07, 0)
    m.add(ellipsoid(body, (0.08, 0.062, 0.07)), g, transform=turn)
    m.add(ellipsoid(body + V(0, -0.01, -0.03), (0.06, 0.045, 0.045)), belly, transform=turn)
    for s in (-1, 1):
        eye = body + V(s * 0.04, 0.06, -0.03)
        m.add(sphere(eye, 0.028), g, transform=turn)
        m.add(sphere(eye + V(0, 0.004, -0.017), 0.017), shiny("FFFFFF"), transform=turn)
        m.add(sphere(eye + V(0, 0.004, -0.028), 0.009), dark, transform=turn)
        m.add(ellipsoid(body + V(s * 0.075, -0.035, 0.01), (0.03, 0.03, 0.05)), g, transform=turn)
        m.add(ellipsoid(body + V(s * 0.05, -0.06, -0.05), (0.022, 0.008, 0.02)), g, transform=turn)
    m.add(torus(body + V(0, 0.012, -0.063), 0.035, 0.003, axis="z", arc=0.36, start=0.57, segments=16, ring_segments=6), col("2E5A22"), transform=turn)
    for s in (-1, 1):
        m.add(ellipsoid(body + V(s * 0.03, 0.02, -0.064), (0.008, 0.006, 0.004)), col("F28AA0"), transform=turn)
    return [m.build()]


def dragon():
    m = Model("piece_dragon")
    b = y0()
    red, belly, wing, dark, horn = col("D64B3A"), col("F2B632"), col("8E3B6B"), shiny("15100E"), col("F6EFDD")
    body = V(-0.03, b + 0.075, 0.0)
    m.add(ellipsoid(body, (0.12, 0.065, 0.072)), red)
    m.add(ellipsoid(body + V(0.01, -0.02, -0.035), (0.09, 0.04, 0.045)), belly)
    head = V(0.14, b + 0.115, -0.005)
    m.add(sphere(head, 0.062), red)
    m.add(ellipsoid(head + V(0.055, -0.02, 0), (0.05, 0.035, 0.042)), red)
    for s in (-1, 1):
        m.add(sphere(head + V(0.098, -0.008, s * 0.016), 0.006), dark)
        m.add(lathe([(0.0, 0), (0.012, 0), (0.0, 0.04)], segments=10), horn,
              transform=Matrix.Translation(head + V(-0.02, 0.045, s * 0.03)) @ Matrix.Rotation(math.radians(-35), 4, "Z"))
    eye = head + V(0.022, 0.018, -0.05)
    m.add(sphere(eye, 0.018), shiny("FFFFFF"))
    m.add(sphere(eye + V(0.005, 0, -0.012), 0.011), dark)
    m.add(sphere(eye + V(0.0, 0.006, -0.02), 0.004), glow("FFFFFF"))
    m.add(ellipsoid(head + V(0.02, -0.01, -0.058), (0.012, 0.007, 0.004)), col("F28AA0"))
    # wings folded on the back
    for s in (-1, 1):
        root = body + V(0.02, 0.05, s * 0.03)
        m.add(hull([root, root + V(-0.11, 0.07, s * 0.01), root + V(-0.06, 0.02, s * 0.015), root + V(-0.13, 0.03, s * 0.012),
                    root + V(0.0, 0.0, s * 0.012)], bevel=0.003), wing)
    for k in range(5):
        p = body + V(-0.08 + k * 0.04, 0.062 - abs(k - 2) * 0.004, 0)
        m.add(lathe([(0.0, 0), (0.012, 0), (0.0, 0.022)], segments=8), belly, transform=Matrix.Translation(p))
    tail = bezier(body + V(-0.11, -0.01, 0), body + V(-0.17, -0.02, 0), body + V(-0.2, 0.04, 0), body + V(-0.205, 0.075, -0.01), 14)
    m.add(sweep(tail, 0.026, radius_end=0.008, segments=12), red)
    m.add(prism([(-0.02, 0), (0.02, 0), (0, 0.035)], -0.006, 0.006), belly, transform=Matrix.Translation(tail[-1]))
    for s in (-1, 1):
        m.add(ellipsoid(body + V(0.05, -0.06, s * 0.045), (0.025, 0.018, 0.02)), red)
        m.add(ellipsoid(body + V(-0.07, -0.06, s * 0.045), (0.028, 0.018, 0.02)), red)
    return [m.build()]


def dragonegg():
    m = Model("piece_dragonegg")
    b = y0()
    nest = col("C9A36A")
    m.add(torus((0, b + 0.016, 0), 0.06, 0.018, segments=24, ring_segments=8), nest)
    for k in range(10):
        a = k * 0.63
        p = V(math.cos(a) * 0.06, b + 0.02 + (k % 3) * 0.004, math.sin(a) * 0.06)
        m.add(rod(p - V(math.sin(a), 0, -math.cos(a)) * 0.03, p + V(math.sin(a), 0.01, -math.cos(a)) * 0.03, 0.0035, segments=5), col("B58A4F"))
    egg = lathe([(0.0, 0), (0.04, 0.006), (0.066, 0.04), (0.075, 0.09), (0.065, 0.14), (0.04, 0.175), (0.0, 0.19)], segments=32)
    egg = displace(egg, 0.004, freq=60, seed=3)
    m.add(egg, shiny("7A5BA6"), transform=Matrix.Translation((0, b + 0.012, 0)))
    for k in range(26):
        a = k * 2.39996
        hh = 0.03 + (k * 0.37 % 1.0) * 0.14
        rr = 0.075 * math.sin(math.pi * hh / 0.19) + 0.001
        m.add(sphere((math.cos(a) * rr, b + 0.012 + hh, math.sin(a) * rr), 0.006, scale=(1, 1, 1), segments=8, rings=5), metal("F2B632"))
    return [m.build()]


# ============================================================================ padding

def paper():
    m = Model("piece_paper")
    b = y0()
    c = V(0, b + 0.11, 0)
    ball = ico(c, 0.105, subdiv=3)
    bm, sm = ball
    for v in bm.verts:  # squash slightly into a lumpy ball
        d = v.co - c
        v.co = c + V(d.x * 1.03, d.y * 0.98, d.z * 0.95)
    ball = displace((bm, sm), 0.022, freq=14, seed=1, flat=True)
    ball = displace(ball, 0.008, freq=40, seed=7, flat=True)
    m.add(ball, matte("EFE6D2"), smooth=False)
    return [m.build()]


def bubble():
    m = Model("piece_bubble")
    b = y0()
    film = glass("CFE8F5")
    m.add(box((-0.105, b, -0.1), (0.105, b + 0.21, 0.1), bevel=0.03, segments=4), film)
    m.add_all(bumps_on_face((0, b + 0.105, -0.1), (0.18, 0.18), "-z", 4, 4, 0.019, 0.012), glass("E6F4FC"))
    m.add_all(bumps_on_face((0, b + 0.21, 0), (0.18, 0.17), "y", 4, 4, 0.019, 0.012), glass("E6F4FC"))
    m.add(box((-0.106, b + 0.1, -0.102), (0.106, b + 0.11, -0.098)), glass("FFFFFF"))
    return [m.build()]


def foam():
    m = Model("piece_foam")
    b = y0()
    f = matte("8FB7C9")
    m.add(box((-0.112, b, -0.11), (0.112, b + 0.226, 0.11), bevel=0.018, segments=3), f)
    m.add_all(bumps_on_face((0, b + 0.113, -0.11), (0.2, 0.2), "-z", 4, 4, 0.024, 0.012), matte("7FA8BC"))
    return [m.build()]


def shards():
    """Generic pile of broken pieces; material slot recoloured per item in Unity."""
    m = Model("piece_shards")
    b = y0()
    import random
    rnd = random.Random(5)
    for k in range(9):
        x = rnd.uniform(-0.08, 0.08)
        z = rnd.uniform(-0.06, 0.06)
        h = rnd.uniform(0.02, 0.06)
        w = rnd.uniform(0.03, 0.06)
        pts = [(x + rnd.uniform(-w, w) * 0.5, b, z + rnd.uniform(-w, w) * 0.5) for _ in range(4)]
        pts.append((x, b + h, z))
        m.add(hull(pts), shiny("FFFFFF"))
    return [m.build()]


def puddle():
    m = Model("piece_puddle")
    b = y0()
    pts = []
    for k in range(20):
        a = k / 20 * 2 * math.pi
        r = 0.1 + 0.02 * math.sin(a * 3) + 0.012 * math.cos(a * 5)
        pts.append((math.cos(a) * r, b + 0.006, math.sin(a) * r * 0.8))
    m.add(hull(pts + [(p[0] * 0.9, b, p[2] * 0.9) for p in pts]), glass("FFFFFF"))
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
    m = Model("strap")
    band, buckle = col("2E4A7A"), metal("D7DCE0")
    w = 0.125  # half width of one cell (scaled by item width in Unity)
    m.add(box((-w - 0.004, -0.018, -0.128), (w + 0.004, 0.018, -0.12)), band)
    for s in (-1, 1):
        m.add(box((s * (w + 0.004) - 0.004, -0.018, -0.128), (s * (w + 0.004) + 0.004, 0.018, 0.13)), band)
    m.add(box((-0.022, -0.026, -0.134), (0.022, 0.026, -0.126), bevel=0.003), buckle)
    m.add(box((-0.014, -0.018, -0.136), (0.014, 0.018, -0.133)), band)
    return [m.build()]


ALL = {
    "teacup": teacup, "books": books, "teddy": teddy, "vase": vase, "bowling": bowling, "armadillo": armadillo,
    "armadillo_awake": armadillo_awake, "magnet": magnet, "potion": potion, "cake": cake, "balloon": balloon,
    "cactus": cactus, "robot": robot, "iceswan": iceswan, "lavalamp": lavalamp, "bouncy": bouncy,
    "snowglobe": snowglobe, "frog": frog, "dragon": dragon, "dragonegg": dragonegg,
    "paper": paper, "bubble": bubble, "foam": foam, "shards": shards, "puddle": puddle,
    "divider": divider, "shelf": shelf, "strap": strap,
}
