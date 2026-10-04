"""Journey set pieces and props (truck, road kit, scenery, depot, doorstep, courier, ship, plane,
catapult, reveal room). Unity space; each prop's origin is where it touches the ground unless noted.
"""
import math
import random

from mathutils import Matrix

from hwc_lib import (Model, V, bezier, box, cbox, col, cyl, decal, displace, ellipsoid, empty, extrude_outline,
                     glass, glow, hull, ico, lathe, matte, metal, prism, rod, rotate_about, shade, shell, shiny,
                     sphere, sweep, tex, torus)

RED = "D9483B"
CREAM = "F3E9D2"
INK = "1F2A44"
TEAL = "2F8F8B"


# ============================================================================ truck

def truck_cab():
    """Cab of the delivery truck. Origin: where the cab meets the bed, at bed-floor height (y=0).
    The truck drives toward +x; the camera sees its -z side."""
    m = Model("truck_cab")
    red, cream, dark, chrome = shiny(RED), shiny(CREAM), col("2A2628"), metal("D7DCE0")
    gl = glass("BFD9E8")
    # cabin body
    m.add(box((0.0, -0.35, -0.72), (1.25, 0.95, 0.72), bevel=0.09, segments=4), red)
    m.add(box((1.15, -0.35, -0.7), (1.85, 0.38, 0.7), bevel=0.08, segments=4), red)          # hood
    m.add(box((1.8, -0.3, -0.62), (1.9, 0.3, 0.62), bevel=0.04), chrome)                    # grille frame
    for k in range(6):
        m.add(box((1.86, -0.25 + k * 0.09, -0.55), (1.91, -0.22 + k * 0.09, 0.55)), dark)
    for s in (-1, 1):
        m.add(cyl((1.84, 0.2, s * 0.5), 0.09, 0.08, axis="x", segments=24, bevel=0.01), chrome)
        m.add(cyl((1.885, 0.2, s * 0.5), 0.07, 0.02, axis="x", segments=24), glow("FFF2C4"))
    # windows
    m.add(box((0.25, 0.45, -0.73), (1.05, 0.85, -0.715), bevel=0.02), gl)
    m.add(box((1.12, 0.45, -0.66), (1.2, 0.85, 0.66), bevel=0.02), gl, transform=rotate_about((1.16, 0.45, 0), "Z", -14))
    m.add(box((0.1, 0.45, -0.6), (0.12, 0.85, 0.6)), gl)
    # cream stripe and door logo
    m.add(box((0.02, 0.18, -0.735), (1.84, 0.3, -0.72), bevel=0.01), cream)
    m.add(box((0.4, -0.25, -0.738), (0.82, 0.17, -0.735)), decal("logo"), uv_scale=1 / 0.42, uv_offset=(0.5 - 0.61 / 0.42, 0.5 + 0.04 / 0.42))
    m.add(box((0.98, -0.05, -0.75), (1.06, 0.0, -0.73), bevel=0.01), chrome)                # door handle
    # mirror, roof light, bumper
    m.add(rod((1.1, 0.7, -0.74), (1.2, 0.72, -0.86), 0.015), dark)
    m.add(box((1.15, 0.6, -0.92), (1.25, 0.78, -0.86), bevel=0.02), red)
    m.add(box((0.4, 0.95, -0.15), (0.8, 1.05, 0.15), bevel=0.04), shiny("F2B632"))
    m.add(box((1.75, -0.42, -0.74), (1.95, -0.3, 0.74), bevel=0.04), chrome)
    # chassis under the cab
    m.add(box((-0.1, -0.48, -0.55), (1.8, -0.35, 0.55)), dark)
    # fenders
    for x in (1.35,):
        m.add(torus((x, -0.35, -0.69), 0.36, 0.05, axis="z", arc=0.5, segments=20, ring_segments=8), red)
    return [m.build()]


def truck_bed():
    """3.2 m flat bed behind the cab. Origin at the bed's front-centre (x=0 where the cab begins),
    floor top at y=0. The bed extends to x = -3.2. Camera side (-z) is open."""
    m = Model("truck_bed")
    wood, red, dark = tex("wood"), shiny(RED), col("2A2628")
    L = 3.2
    m.add(box((-L, -0.09, -0.7), (0.0, 0.0, 0.72), bevel=0.01), wood, uv_scale=1.4)
    m.add(box((-L, -0.2, -0.72), (0.0, -0.09, 0.72), bevel=0.02), red)
    m.add(box((-L - 0.02, -0.48, -0.5), (0.0, -0.2, 0.5)), dark)
    # far rail with posts and planks
    for k in range(6):
        x = -L + 0.08 + k * (L - 0.16) / 5
        m.add(box((x - 0.04, 0.0, 0.64), (x + 0.04, 0.55, 0.72), bevel=0.012), red)
    for y in (0.18, 0.4):
        m.add(box((-L, y, 0.66), (0.0, y + 0.1, 0.7), bevel=0.01), wood, uv_scale=1.4)
    # tail gate (left end) and short front rail
    m.add(box((-L - 0.06, -0.2, -0.7), (-L, 0.4, 0.72), bevel=0.015), red)
    m.add(box((-0.06, 0.0, -0.7), (0.0, 0.5, 0.72), bevel=0.015), red)
    # rear lights
    m.add(box((-L - 0.07, -0.16, -0.66), (-L - 0.05, -0.06, -0.5), bevel=0.01), glow("FF5A4A"))
    # fender over the rear wheel
    m.add(torus((-L + 0.75, -0.2, -0.71), 0.38, 0.05, axis="z", arc=0.5, segments=20, ring_segments=8), red)
    return [m.build()]


def truck_wheel():
    m = Model("truck_wheel")
    tire, hub, cap = col("2A2628"), metal("C9CED4"), shiny(RED)
    m.add(cyl((0, 0, 0), 0.32, 0.22, axis="z", segments=32, bevel=0.06), tire)
    m.add(cyl((0, 0, -0.06), 0.19, 0.12, axis="z", segments=28, bevel=0.02), hub)
    m.add(cyl((0, 0, -0.12), 0.07, 0.02, axis="z", segments=20), cap)
    for k in range(5):
        a = k / 5 * 2 * math.pi
        m.add(cyl((math.cos(a) * 0.12, math.sin(a) * 0.12, -0.125), 0.018, 0.012, axis="z", segments=10), col("8A8F96"))
    return [m.build()]


def parcel_stack():
    """A few other parcels and a mail sack to dress the truck bed and depot."""
    m = Model("parcel_stack")
    kraft = tex("kraft")
    m.add(box((-0.3, 0.0, -0.25), (0.3, 0.45, 0.25), bevel=0.01), kraft, uv_scale=2)
    m.add(box((-0.25, 0.45, -0.2), (0.2, 0.75, 0.2), bevel=0.01), kraft, uv_scale=2, transform=rotate_about((0, 0.45, 0), "Y", 12))
    m.add(box((-0.31, 0.2, -0.255), (0.31, 0.26, -0.25)), tex("tape_kraft"), uv_scale=(1 / 0.5, 1 / 0.06))
    m.add(box((0.32, 0.0, -0.2), (0.62, 0.22, 0.15), bevel=0.01), kraft, uv_scale=2)
    sack = ellipsoid((-0.75, 0.25, 0.0), (0.28, 0.25, 0.22))
    m.add(displace(sack, 0.02, freq=6, seed=4), col("C9B48A"))
    m.add(cyl((-0.75, 0.5, 0.0), 0.06, 0.08, axis="y", segments=12), col("8A6A4A"))
    return [m.build()]


# ============================================================================ road & scenery

def road_tile():
    """2 m road section centred on x; road surface top at y=0, spans z -1.6..1.6 with verges."""
    m = Model("road_tile")
    asphalt, line, curb, grass = col("5E5A5C"), matte("F2E6C4"), col("BDB3A6"), col("8DB36B")
    m.add(box((-1.0, -0.12, -1.4), (1.0, 0.0, 1.4)), asphalt)
    m.add(box((-0.55, 0.0, 0.02), (0.55, 0.004, 0.12)), line)
    for s in (-1, 1):
        m.add(box((-1.0, -0.12, s * 1.4 - (0.12 if s > 0 else 0)), (1.0, 0.06, s * 1.4 + (0.0 if s > 0 else 0.12)), bevel=0.01), curb)
    m.add(box((-1.0, -0.1, 1.52), (1.0, 0.04, 4.5)), grass)
    m.add(box((-1.0, -0.1, -4.5), (1.0, 0.04, -1.52)), grass)
    return [m.build()]


def speedbump():
    m = Model("prop_speedbump")
    for k in range(6):
        c = "F2B632" if k % 2 == 0 else "2A2628"
        z0 = -1.35 + k * 0.45
        prim = lathe([(0.0, -0.001), (0.3, -0.001), (0.2, 0.05), (0.0, 0.1)], segments=24)
        m.add(prim, shiny(c), transform=Matrix.Translation((0, 0, z0 + 0.225)) @ Matrix.Diagonal(V(1, 1, 0.75, 1)) @ Matrix.Rotation(math.pi / 2, 4, "X") @ Matrix.Rotation(math.pi / 2, 4, "X").inverted())
    # simpler: one long rounded hump with painted stripes
    return [m.build()]


def speedbump2():
    m = Model("prop_speedbump")
    for k in range(6):
        c = "F2B632" if k % 2 == 0 else "2A2628"
        z0 = -1.35 + k * 0.45
        m.add(cyl((0, -0.06, z0 + 0.225), 0.22, 0.45, axis="z", segments=28), shiny(c), transform=Matrix.Translation((0, 0, 0)) @ Matrix.Diagonal(V(1.0, 0.55, 1.0, 1)))
    return [m.build()]


def pothole():
    m = Model("prop_pothole")
    rnd = random.Random(3)
    pts = []
    for k in range(16):
        a = k / 16 * 2 * math.pi
        r = 0.42 + rnd.uniform(-0.06, 0.06)
        pts.append((math.cos(a) * r, math.sin(a) * r * 0.85))
    m.add(extrude_outline(pts, -0.02, 0.0, bevel=0.0), col("2A2628"),
          transform=Matrix.Rotation(math.pi / 2, 4, "X"))
    for k in range(8):  # crumbled rim chunks
        a = k / 8 * 2 * math.pi + rnd.uniform(0, 0.3)
        p = V(math.cos(a) * 0.47, 0.005, math.sin(a) * 0.4)
        m.add(ico(p, 0.04 + rnd.uniform(0, 0.03), 1), col("6B6466"))
    return [m.build()]


def cobbles():
    """1 m strip of cobblestones (z -1.4..1.4)."""
    m = Model("prop_cobbles")
    rnd = random.Random(8)
    for i in range(4):
        for j in range(11):
            x = -0.5 + (i + 0.5) * 0.25 + (0.125 if j % 2 else 0)
            if x > 0.5:
                x -= 1.0
            z = -1.4 + (j + 0.5) * 0.255
            c = rnd.choice(("8A8079", "9C918A", "7D746E", "A39A92"))
            m.add(box((x - 0.11, -0.02, z - 0.11), (x + 0.11, 0.03 + rnd.uniform(0, 0.015), z + 0.11), bevel=0.03, segments=2), col(c))
    return [m.build()]


def tree_round():
    m = Model("tree_round")
    rnd = random.Random(1)
    m.add(cyl((0, 0.6, 0), 0.12, 1.2, axis="y", radius2=0.09, segments=14), col("7A5236"))
    for k in range(5):
        p = V(rnd.uniform(-0.35, 0.35), 1.6 + rnd.uniform(-0.15, 0.4), rnd.uniform(-0.3, 0.3))
        m.add(displace(sphere(p, rnd.uniform(0.45, 0.65), segments=20, rings=12), 0.05, 3, k), col(rnd.choice(("6FA35A", "7BAE5E", "86B860"))))
    return [m.build()]


def tree_pine():
    m = Model("tree_pine")
    m.add(cyl((0, 0.3, 0), 0.1, 0.6, axis="y", segments=12), col("6E4528"))
    for k in range(4):
        y = 0.5 + k * 0.55
        r = 0.85 - k * 0.17
        m.add(lathe([(0.0, 0.0), (r, 0.0), (r * 0.6, 0.25), (0.0, 0.85)], segments=14, smooth=False), col("3E7A4F" if k % 2 else "4A8A5A"),
              transform=Matrix.Translation((0, y, 0)))
    return [m.build()]


def bush():
    m = Model("prop_bush")
    rnd = random.Random(2)
    for k in range(4):
        p = V(rnd.uniform(-0.3, 0.3), 0.25 + rnd.uniform(0, 0.12), rnd.uniform(-0.15, 0.15))
        m.add(displace(sphere(p, rnd.uniform(0.25, 0.35), segments=16, rings=10), 0.03, 4, k), col(rnd.choice(("5E9E4A", "6FAA55"))))
    for k in range(5):
        p = V(rnd.uniform(-0.35, 0.35), rnd.uniform(0.35, 0.55), rnd.uniform(-0.3, -0.15))
        m.add(sphere(p, 0.04, segments=8, rings=6), shiny(rnd.choice(("F07AA8", "F7D24A", "FFFFFF"))))
    return [m.build()]


def house(variant):
    colors = [("F0D9B5", "C8693F", "2F8F8B"), ("CFE3E8", "8E3B6B", "F2B632"), ("F4E3C2", "3E7A4F", "D9483B")][variant]
    wall, roof, door = colors
    m = Model(f"house_{variant}")
    w, h, d = 3.2, 2.4, 2.4
    m.add(box((-w / 2, 0, -d / 2), (w / 2, h, d / 2), bevel=0.04), col(wall))
    m.add(prism([(-w / 2 - 0.2, h), (w / 2 + 0.2, h), (0, h + 1.4)], -d / 2 - 0.2, d / 2 + 0.2, bevel=0.03), shiny(roof))
    m.add(box((0.7, h + 0.3, 0.2), (1.05, h + 1.2, 0.55), bevel=0.02), col("A8574A"))
    m.add(box((-0.35, 0, -d / 2 - 0.03), (0.35, 1.35, -d / 2 + 0.01), bevel=0.03), shiny(door))
    m.add(sphere((0.22, 0.68, -d / 2 - 0.05), 0.04), metal("E0B54A"))
    for x in (-1.05, 1.05):
        m.add(box((x - 0.38, 0.9, -d / 2 - 0.03), (x + 0.38, 1.7, -d / 2 + 0.01), bevel=0.02), col(CREAM))
        m.add(box((x - 0.3, 0.98, -d / 2 - 0.04), (x + 0.3, 1.62, -d / 2 - 0.02)), glow("FFE2A8") if variant == 1 else glass("BFD9E8"))
        m.add(box((x - 0.45, 0.85, -d / 2 - 0.1), (x + 0.45, 0.92, -d / 2 + 0.02)), col(CREAM))
    return [m.build()]


def fence():
    m = Model("prop_fence")
    for k in range(5):
        x = -0.8 + k * 0.4
        m.add(prism([(x - 0.06, 0.0), (x + 0.06, 0.0), (x + 0.06, 0.7), (x, 0.78), (x - 0.06, 0.7)], -0.025, 0.025), col("FBF6EA"))
    for y in (0.22, 0.52):
        m.add(box((-1.0, y, 0.025), (1.0, y + 0.07, 0.05)), col("FBF6EA"))
    return [m.build()]


def lamppost():
    m = Model("prop_lamppost")
    m.add(cyl((0, 1.4, 0), 0.05, 2.8, axis="y", segments=12), shiny(INK))
    m.add(cyl((0, 0.1, 0), 0.12, 0.2, axis="y", segments=16, bevel=0.02), shiny(INK))
    m.add(rod((0, 2.75, 0), (0.35, 2.85, 0), 0.03), shiny(INK))
    m.add(lathe([(0.0, 0.0), (0.16, 0.0), (0.08, 0.18), (0.0, 0.2)], segments=16), shiny(INK), transform=Matrix.Translation((0.4, 2.7, 0)))
    m.add(sphere((0.4, 2.68, 0), 0.08), glow("FFE2A8"))
    return [m.build()]


def mailbox():
    m = Model("prop_mailbox")
    m.add(cyl((0, 0.45, 0), 0.04, 0.9, axis="y", segments=10), col("6E4528"))
    m.add(box((-0.22, 0.9, -0.14), (0.22, 1.12, 0.14), bevel=0.02), shiny(RED))
    m.add(cyl((0, 1.12, 0), 0.14, 0.44, axis="x", segments=20), shiny(RED))
    m.add(box((0.2, 1.05, 0.14), (0.24, 1.3, 0.16)), shiny("F2B632"))
    return [m.build()]


def hill():
    m = Model("prop_hill")
    m.add(displace(ellipsoid((0, 0, 0), (9, 3, 5), segments=32, rings=16), 0.25, 0.4, 2), col("9BC07A"))
    return [m.build()]


def cloud():
    m = Model("prop_cloud")
    rnd = random.Random(5)
    for k in range(6):
        p = V(rnd.uniform(-1.2, 1.2), rnd.uniform(-0.1, 0.4), rnd.uniform(-0.4, 0.4))
        m.add(sphere(p, rnd.uniform(0.45, 0.8), segments=16, rings=10), matte("FFFFFF"))
    return [m.build()]


# ============================================================================ depot

def conveyor():
    """1 m conveyor segment (scaled along x in Unity). Belt top at y=0, centred on x."""
    m = Model("prop_conveyor")
    belt, frame, roller = col("3C3A3D"), shiny("E0A23A"), metal("C9CED4")
    m.add(box((-0.5, -0.05, -0.42), (0.5, 0.0, 0.42)), belt)
    for k in range(5):
        x = -0.4 + k * 0.2
        m.add(cyl((x, -0.075, 0), 0.03, 0.82, axis="z", segments=12), roller)
    # far rail stands above the belt; the camera-side rail sits below it so the box stays visible
    m.add(box((-0.5, -0.16, 0.44 - 0.03), (0.5, 0.05, 0.44 + 0.03)), frame)
    m.add(box((-0.5, -0.17, -0.47), (0.5, -0.06, -0.41)), frame)
    return [m.build()]


def depot_bin():
    m = Model("prop_bin")
    blue = shiny("4E7FA8")
    m.add(box((-0.85, 0.0, -0.6), (0.85, 0.06, 0.6), bevel=0.02), blue)
    m.add(box((-0.85, 0.0, 0.54), (0.85, 0.55, 0.6), bevel=0.02), blue)
    m.add(box((-0.85, 0.0, -0.6), (-0.79, 0.55, 0.6), bevel=0.02), blue)
    m.add(box((0.79, 0.0, -0.6), (0.85, 0.55, 0.6), bevel=0.02), blue)
    m.add(box((-0.85, 0.0, -0.6), (0.85, 0.12, -0.54), bevel=0.02), blue)
    for x in (-0.6, 0.6):
        m.add(cyl((x, -0.08, -0.45), 0.08, 0.06, axis="z", segments=16), col("2A2628"))
        m.add(cyl((x, -0.08, 0.45), 0.08, 0.06, axis="z", segments=16), col("2A2628"))
    return [m.build()]


def chute():
    """2 m metal slide (rotated in Unity), top surface at y=0, centred on x."""
    m = Model("prop_chute")
    m.add(box((-1.0, -0.05, -0.45), (1.0, 0.0, 0.45)), metal("B8BCC2"))
    m.add(box((-1.0, -0.05, 0.46 - 0.02), (1.0, 0.18, 0.46 + 0.02), bevel=0.01), shiny("E0A23A"))
    m.add(box((-1.0, -0.12, -0.48), (1.0, -0.02, -0.44), bevel=0.01), shiny("E0A23A"))
    return [m.build()]


def bumper():
    m = Model("prop_bumper")
    m.add(box((-0.08, 0.0, -0.5), (0.08, 0.45, 0.5), bevel=0.04), shiny(RED))
    for k in range(4):
        m.add(box((-0.081, 0.05 + k * 0.1, -0.5), (0.081, 0.09 + k * 0.1, 0.5)), shiny("F2B632"))
    return [m.build()]


def robot_arm():
    """Industrial arm in parts: Base (origin at floor), Upper (pivot at shoulder), Fore (pivot at elbow),
    Grip (pivot at wrist). Unity poses the joints."""
    yel, dark, chrome = shiny("E0A23A"), col("3A3638"), metal("C9CED4")
    base = Model("arm_base")
    base.add(cyl((0, 0.08, 0), 0.38, 0.16, axis="y", segments=32, bevel=0.03), dark)
    base.add(cyl((0, 0.45, 0), 0.22, 0.6, axis="y", segments=28, bevel=0.04), yel)
    base.add(cyl((0, 0.8, 0), 0.16, 0.42, axis="z", segments=24, bevel=0.03), dark)
    b = base.build()
    up = Model("Upper")
    up.add(box((-0.11, 0.0, -0.11), (0.11, 1.1, 0.11), bevel=0.05, segments=3), yel)
    up.add(cyl((0, 1.1, 0), 0.14, 0.3, axis="z", segments=24, bevel=0.02), dark)
    u = up.build(origin=(0, 0.8, 0), parent=b)
    fo = Model("Fore")
    fo.add(box((-0.08, 0.0, -0.08), (0.08, 0.9, 0.08), bevel=0.04, segments=3), yel)
    fo.add(cyl((0, 0.9, 0), 0.1, 0.22, axis="z", segments=20, bevel=0.02), dark)
    f = fo.build(origin=(0, 1.9, 0), parent=u, parent_origin=(0, 0.8, 0))
    gr = Model("Grip")
    gr.add(box((-0.08, 0.0, -0.18), (0.08, 0.1, 0.18), bevel=0.02), chrome)
    for s in (-1, 1):
        gr.add(box((-0.04, 0.0, s * 0.2 - 0.03), (0.04, -0.25, s * 0.2 + 0.03), bevel=0.01), dark)
        gr.add(box((-0.06, -0.25, s * 0.2 - 0.04), (0.06, -0.2, s * 0.18), bevel=0.01), col("2A2628"))
    gr.build(origin=(0, 2.8, 0), parent=f, parent_origin=(0, 1.9, 0))
    return [b]


def rack():
    m = Model("prop_rack")
    blue, kraft = shiny("4E7FA8"), tex("kraft")
    rnd = random.Random(7)
    for x in (-1.4, 1.4):
        for z in (-0.4, 0.4):
            m.add(box((x - 0.05, 0, z - 0.05), (x + 0.05, 3.2, z + 0.05)), blue)
    for y in (0.3, 1.3, 2.3):
        m.add(box((-1.45, y, -0.45), (1.45, y + 0.08, 0.45)), shiny("E0A23A"))
        x = -1.3
        while x < 1.1:
            w = rnd.uniform(0.3, 0.6)
            h = rnd.uniform(0.3, 0.75)
            m.add(box((x, y + 0.08, -0.35), (x + w, y + 0.08 + h, 0.35), bevel=0.01), kraft, uv_scale=2)
            x += w + 0.06
    return [m.build()]


def hanging_lamp():
    m = Model("prop_hanglamp")
    m.add(rod((0, 0, 0), (0, 1.5, 0), 0.01), col("2A2628"))
    m.add(lathe([(0.03, 0.0), (0.06, -0.02), (0.32, -0.25), (0.33, -0.27)], segments=28), shiny("2F5E5A"))
    m.add(sphere((0, -0.16, 0), 0.08), glow("FFE2A8"))
    return [m.build()]


def depot_wall():
    """10 m wall section with high windows, origin at floor level, wall face at z=0."""
    m = Model("depot_wall")
    m.add(box((-5, 0, 0), (5, 7, 0.3)), col("C9BFAE"))
    m.add(box((-5, 0, -0.02), (5, 1.2, 0.0)), col("8C8682"))
    m.add(box((-5, 1.2, -0.03), (5, 1.32, 0.0)), shiny("E0A23A"))
    for x in (-3.0, 0.0, 3.0):
        m.add(box((x - 1.1, 3.8, -0.03), (x + 1.1, 5.8, 0.0), bevel=0.03), col("6B6466"))
        m.add(box((x - 1.0, 3.9, -0.04), (x + 1.0, 5.7, -0.02)), glow("E8F0F6"))
        m.add(box((x - 0.02, 3.9, -0.05), (x + 0.02, 5.7, -0.03)), col("6B6466"))
        m.add(box((x - 1.0, 4.78, -0.05), (x + 1.0, 4.82, -0.03)), col("6B6466"))
    return [m.build()]


def depot_floor():
    m = Model("depot_floor")
    m.add(box((-5, -0.2, -4), (5, 0.0, 4)), col("8C8682"))
    for z in (-1.4, 1.4):
        m.add(box((-5, 0.0, z - 0.05), (5, 0.003, z + 0.05)), matte("F2B632"))
    return [m.build()]


# ============================================================================ doorstep

def step():
    """One stair step: tread top at y=0 (front edge at x=0, extends +x by 0.3), riser below."""
    m = Model("prop_step")
    m.add(box((0.0, -0.2, -0.9), (0.32, 0.0, 0.9), bevel=0.015), col("C9B9A5"))
    m.add(box((-0.02, -0.04, -0.91), (0.32, 0.0, 0.91), bevel=0.01), col("E0D3C2"))
    return [m.build()]


def porch():
    """Porch floor top at y=0, from x=0 to 4, house wall at z=1.4."""
    m = Model("porch")
    wood = tex("wood")
    m.add(box((0.0, -0.25, -1.0), (4.0, 0.0, 1.4), bevel=0.01), wood, uv_scale=1.2)
    m.add(box((0.0, -1.6, 1.4), (4.6, 3.2, 1.7)), col("F0D9B5"))
    m.add(box((0.0, 3.2, 0.6), (4.6, 3.35, 1.8), bevel=0.02), shiny("C8693F"))
    for x in (0.1, 3.9):
        m.add(box((x - 0.07, 0.0, -0.95), (x + 0.07, 3.2, -0.81), bevel=0.02), col(CREAM))
    # door
    m.add(box((1.6, 0.0, 1.36), (2.6, 2.2, 1.42), bevel=0.03), shiny(TEAL))
    m.add(box((1.75, 1.35, 1.34), (2.45, 1.95, 1.37)), glass("BFD9E8"))
    m.add(sphere((2.45, 1.05, 1.32), 0.045), metal("E0B54A"))
    m.add(torus((2.1, 1.55, 1.33), 0.08, 0.012, axis="z", segments=16, ring_segments=6), metal("E0B54A"))
    m.add(box((1.45, 0.0, 1.36), (2.75, 2.35, 1.4), bevel=0.03), col(CREAM))
    # doormat, flower pots, window
    m.add(box((1.55, 0.0, 0.3), (2.65, 0.02, 1.2), bevel=0.01), col("A8574A"))
    for x in (0.6, 3.4):
        m.add(lathe([(0.0, 0), (0.16, 0), (0.2, 0.3), (0.0, 0.3)], segments=18), col("C8693F"), transform=Matrix.Translation((x, 0, 1.0)))
        m.add(displace(sphere((x, 0.42, 1.0), 0.2, segments=14, rings=8), 0.03, 6, 1), col("5E9E4A"))
        for k in range(4):
            m.add(sphere((x + math.cos(k) * 0.12, 0.5 + (k % 2) * 0.06, 1.0 - 0.12 + math.sin(k) * 0.05), 0.035), shiny(("F07AA8", "F7D24A")[k % 2]))
    m.add(box((3.0, 1.0, 1.35), (4.1, 2.1, 1.4), bevel=0.02), col(CREAM))
    m.add(box((3.08, 1.08, 1.33), (4.02, 2.02, 1.36)), glow("FFE2A8"))
    return [m.build()]


def courier():
    """Dash the courier, in parts for procedural animation. Origin at the feet; faces -z (camera).
    Children: Body, Head, ArmL, ArmR (pivots at shoulders), LegL, LegR (pivots at hips)."""
    uni, skin, cap, dark = col("D9483B"), col("E8B48C"), shiny(INK), col("2A2628")
    root = empty("courier")
    body = Model("Body")
    body.add(ellipsoid((0, 1.05, 0), (0.24, 0.34, 0.18)), uni)
    body.add(box((-0.22, 0.95, -0.19), (0.22, 1.02, 0.17), bevel=0.02), shiny("F2B632"))
    body.add(box((-0.08, 1.15, -0.185), (0.08, 1.25, -0.17), bevel=0.01), col(CREAM))
    b = body.build(parent=root)
    head = Model("Head")
    head.add(sphere((0, 1.58, 0), 0.19), skin)
    head.add(ellipsoid((0, 1.72, -0.01), (0.2, 0.08, 0.2)), cap)
    head.add(box((-0.13, 1.66, -0.3), (0.13, 1.69, -0.12), bevel=0.02), cap)
    for s in (-1, 1):
        head.add(sphere((s * 0.065, 1.6, -0.165), 0.024), shiny("15100E"))
        head.add(sphere((s * 0.19, 1.57, 0), 0.04), skin)
    head.add(torus((0, 1.52, -0.17), 0.05, 0.008, axis="z", arc=0.4, start=0.55, segments=12, ring_segments=6), col("8A3B2E"))
    head.build(origin=(0, 1.38, 0), parent=root)
    for s, name in ((-1, "ArmL"), (1, "ArmR")):
        a = Model(name)
        a.add(ellipsoid((s * 0.3, 1.1, 0), (0.07, 0.24, 0.07)), uni)
        a.add(sphere((s * 0.3, 0.84, 0), 0.07), skin)
        a.build(origin=(s * 0.27, 1.3, 0), parent=root)
        l = Model("LegL" if s < 0 else "LegR")
        l.add(ellipsoid((s * 0.11, 0.42, 0), (0.08, 0.3, 0.08)), col(INK))
        l.add(ellipsoid((s * 0.11, 0.07, -0.05), (0.09, 0.07, 0.15)), dark)
        l.build(origin=(s * 0.11, 0.72, 0), parent=root)
    return [root]


def street_tile():
    m = Model("street_tile")
    m.add(box((-1.0, -0.15, -3.0), (1.0, 0.0, -0.9)), col("6B6466"))
    m.add(box((-1.0, -0.15, -0.9), (1.0, 0.12, 0.6)), col("C9C0B5"))
    m.add(box((-1.0, 0.12, -0.9), (1.0, 0.13, -0.85)), col("A39A92"))
    m.add(box((-1.0, -0.15, 0.6), (1.0, 0.1, 5.0)), col("8DB36B"))
    return [m.build()]


# ============================================================================ ship, plane, catapult

def ship_deck():
    """Ferry deck section around the box. Deck top at y=0, spans x -3..3."""
    m = Model("ship_deck")
    wood, white, red = tex("wood"), shiny("F4F1EA"), shiny(RED)
    m.add(box((-3.2, -0.2, -1.0), (3.2, 0.0, 1.6)), wood, uv_scale=1.2)
    m.add(box((-3.2, -1.6, -1.05), (3.2, -0.2, 1.65)), white)
    m.add(box((-3.2, -0.6, -1.06), (3.2, -0.4, 1.66)), red)
    for k in range(13):
        x = -3.0 + k * 0.5
        m.add(rod((x, 0.0, 1.5), (x, 0.9, 1.5), 0.025), white)
    m.add(rod((-3.1, 0.9, 1.5), (3.1, 0.9, 1.5), 0.035), white)
    m.add(rod((-3.1, 0.5, 1.5), (3.1, 0.5, 1.5), 0.02), white)
    m.add(torus((1.8, 0.55, 1.45), 0.22, 0.06, axis="z", segments=24, ring_segments=10), red)
    for k in range(4):
        m.add(torus((1.8, 0.55, 1.45), 0.22, 0.062, axis="z", arc=0.08, start=k * 0.25, segments=6, ring_segments=10), white)
    m.add(cyl((-2.4, 1.6, 0.9), 0.35, 3.2, axis="y", segments=24), red)
    m.add(cyl((-2.4, 2.9, 0.9), 0.36, 0.5, axis="y", segments=24), col("2A2628"))
    m.add(box((2.3, 0.0, 0.3), (2.9, 0.5, 0.9), bevel=0.02), wood, uv_scale=2)
    return [m.build()]


def waves():
    """A strip of sea with rolling waves, 12 m long, origin at mean water level."""
    m = Model("prop_waves")
    for k in range(24):
        x = -6 + k * 0.5
        m.add(cyl((x, -0.1, 0), 0.3, 14, axis="z", segments=12), shiny("3F7FA6"))
        m.add(cyl((x + 0.12, 0.12, 0), 0.08, 14, axis="z", segments=8), matte("E8F4FA"))
    return [m.build()]


def plane_hold():
    """Cargo hold of a mail plane, cutaway toward the camera. Floor top at y=0, spans x -3..3."""
    m = Model("plane_hold")
    metal_c, rib, floor = metal("B8BCC2"), shiny("8A8F96"), col("6B6466")
    m.add(box((-3.2, -0.08, -1.0), (3.2, 0.0, 1.3)), floor)
    for k in range(7):
        x = -3.0 + k * 1.0
        m.add(torus((x, 0.8, 0.2), 1.25, 0.04, axis="x", arc=0.5, start=0.25, segments=24, ring_segments=6), rib)
    # far wall with porthole frames
    m.add(box((-3.2, 0.0, 1.3), (3.2, 1.9, 1.38)), metal_c)
    for k in range(5):
        x = -2.4 + k * 1.2
        m.add(torus((x, 1.1, 1.28), 0.22, 0.05, axis="z", segments=24, ring_segments=8), shiny("E0A23A"))
        m.add(cyl((x, 1.1, 1.3), 0.2, 0.02, axis="z", segments=24), glow("CFE6F7"))
    m.add(box((-3.2, 1.9, -0.2), (3.2, 2.0, 1.38)), metal_c)
    # cargo net
    for k in range(9):
        m.add(rod((-3.0 + k * 0.4, 0.0, 1.22), (-3.2 + k * 0.4, 1.6, 1.22), 0.008), col("C9B48A"))
    return [m.build()]


def catapult():
    """Wooden catapult. Children: Arm (pivot at the axle). Origin at ground level, centred."""
    wood, rope, iron = tex("wood"), col("C9B48A"), metal("8A8F96")
    base = Model("catapult")
    for s in (-1, 1):
        base.add(box((-1.6, 0.0, s * 0.7 - 0.08), (1.6, 0.18, s * 0.7 + 0.08), bevel=0.02), wood, uv_scale=1.2)
        base.add(hull([(-0.6, 0.18, s * 0.7 - 0.07), (0.6, 0.18, s * 0.7 - 0.07), (0.0, 1.4, s * 0.7 - 0.07),
                       (-0.6, 0.18, s * 0.7 + 0.07), (0.6, 0.18, s * 0.7 + 0.07), (0.0, 1.4, s * 0.7 + 0.07)], bevel=0.02), wood)
    for x in (-1.4, 1.4):
        base.add(box((x - 0.08, 0.0, -0.78), (x + 0.08, 0.18, 0.78), bevel=0.02), wood, uv_scale=1.2)
    base.add(cyl((0.0, 1.3, 0), 0.08, 1.6, axis="z", segments=16), iron)
    for x in (-1.3, 1.3):
        for z in (-0.78, 0.78):
            base.add(cyl((x, 0.0, z), 0.3, 0.12, axis="z", segments=20, bevel=0.03), wood)
    b = base.build()
    arm = Model("Arm")
    arm.add(box((-0.08, -0.08, -0.12), (2.5, 0.08, 0.12), bevel=0.02), wood, uv_scale=1.2)
    arm.add(box((-0.9, -0.3, -0.35), (-0.1, 0.25, 0.35), bevel=0.04), col("6B6466"))
    for k in range(4):
        arm.add(torus((0.3 + k * 0.4, 0.0, 0.0), 0.1, 0.02, axis="x", segments=12, ring_segments=6), rope)
    a = arm.build(origin=(0.0, 1.3, 0), parent=b)
    # the bucket hangs from the arm tip and is kept level in Unity
    bucket = Model("Bucket")
    bucket.add(box((-0.62, -0.06, -0.3), (0.62, 0.0, 0.3), bevel=0.02), wood, uv_scale=1.2)
    for sx in (-1, 1):
        bucket.add(box((sx * 0.62 - 0.04, -0.06, -0.3), (sx * 0.62 + 0.04, 0.22, 0.3), bevel=0.02), wood, uv_scale=1.2)
    bucket.add(box((-0.62, -0.06, 0.26), (0.62, 0.22, 0.3), bevel=0.02), wood, uv_scale=1.2)
    bucket.add(rod((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), 0.01), rope)
    bucket.build(origin=(2.5, 1.3, 0), parent=a, parent_origin=(0.0, 1.3, 0))
    return [b]


def haystack():
    m = Model("prop_haystack")
    hay = col("E8C46A")
    m.add(displace(ellipsoid((0, 0.5, 0), (1.4, 0.75, 1.1), segments=28, rings=14), 0.08, 5, 3), hay)
    rnd = random.Random(9)
    for k in range(30):
        p = V(rnd.uniform(-1.2, 1.2), rnd.uniform(0.6, 1.1), rnd.uniform(-0.9, 0.2))
        m.add(rod(p, p + V(rnd.uniform(-0.15, 0.15), rnd.uniform(0.05, 0.2), rnd.uniform(-0.1, 0.1)), 0.008), col("D9B04A"))
    return [m.build()]


def tower():
    m = Model("prop_tower")
    stone, roof = col("A39A92"), shiny("4B2E6E")
    m.add(cyl((0, 3.0, 0), 1.2, 6.0, axis="y", radius2=1.0, segments=28), stone)
    m.add(lathe([(0.0, 0.0), (1.45, 0.0), (0.0, 3.0)], segments=28, smooth=False), roof, transform=Matrix.Translation((0, 6.0, 0)))
    for k in range(3):
        y = 1.5 + k * 1.6
        m.add(box((-0.25, y, -1.22), (0.25, y + 0.7, -1.0), bevel=0.05), glow("FFD28A"))
    m.add(sphere((0, 9.1, 0), 0.15), glow("F2B632"))
    m.add(rod((0, 9.0, 0), (0, 9.8, 0), 0.03), metal("E0B54A"))
    return [m.build()]


# ============================================================================ reveal room

def reveal_room():
    """Customer's living room. Table top at y=0 centred on the origin; camera looks from -z."""
    m = Model("reveal_room")
    wood = tex("wood")
    # table with tablecloth
    m.add(box((-1.3, -0.06, -0.8), (1.3, 0.0, 0.8), bevel=0.02), wood, uv_scale=1.2)
    m.add(box((-1.32, -0.004, -0.82), (1.32, 0.002, 0.82), bevel=0.003), col("F3E9D2"))
    for k in range(9):
        m.add(box((-1.32 + k * 0.33, -0.005, -0.825), (-1.32 + k * 0.33 + 0.16, 0.003, 0.825)), col("E57F7F"))
    m.add(box((-1.33, -0.3, -0.83), (1.33, 0.0, -0.81)), col("F3E9D2"))
    for x in (-1.15, 1.15):
        for z in (-0.65, 0.65):
            m.add(box((x - 0.05, -0.85, z - 0.05), (x + 0.05, -0.06, z + 0.05)), col("6E4528"))
    # room
    m.add(box((-6, -0.85, -4), (6, -0.8, 4)), wood, uv_scale=0.8)
    m.add(box((-6, -0.85, 2.5), (6, 4, 2.6)), col("D7C6E0"))
    m.add(box((-6, -0.85, 2.45), (6, 0.1, 2.5)), col("F3E9D2"))
    m.add(box((-2.2, -0.79, -1.8), (2.2, -0.77, 1.6), bevel=0.01), col("2F8F8B"))
    # window with curtains
    m.add(box((-3.6, 0.6, 2.4), (-1.6, 2.6, 2.48), bevel=0.02), col("F3E9D2"))
    m.add(box((-3.5, 0.7, 2.38), (-1.7, 2.5, 2.42)), glow("FFE7C4"))
    for x in (-3.8, -1.45):
        m.add(box((x - 0.18, 0.4, 2.3), (x + 0.18, 2.8, 2.38), bevel=0.04), col("E57F7F"))
    # picture frames, lamp, armchair, plant
    for x, w, h, c in ((0.6, 0.6, 0.45, "F2B632"), (1.5, 0.4, 0.55, "2F8F8B"), (2.3, 0.5, 0.35, "D9483B")):
        m.add(box((x - w / 2, 1.5, 2.42), (x + w / 2, 1.5 + h, 2.47), bevel=0.02), col("6E4528"))
        m.add(box((x - w / 2 + 0.05, 1.55, 2.41), (x + w / 2 - 0.05, 1.45 + h, 2.42)), col(c))
    m.add(box((2.6, -0.8, 1.4), (4.0, -0.3, 2.3), bevel=0.12, segments=4), col("8E3B6B"))
    m.add(box((2.6, -0.3, 2.0), (4.0, 0.5, 2.3), bevel=0.12, segments=4), col("8E3B6B"))
    m.add(lathe([(0.0, 0), (0.2, 0), (0.25, 0.4), (0.0, 0.4)], segments=20), col("C8693F"), transform=Matrix.Translation((-2.6, -0.8, 1.8)))
    for k in range(8):
        a = k / 8 * 2 * math.pi
        tip = V(-2.6 + math.cos(a) * 0.5, 0.4 + (k % 3) * 0.2, 1.8 + math.sin(a) * 0.3)
        m.add(sweep(bezier(V(-2.6, -0.4, 1.8), V(-2.6, 0.0, 1.8), tip + V(0, 0.2, 0), tip, 8), 0.05, radius_end=0.01, profile_scale=(1, 0.35)), col("5E9E4A"))
    m.add(cyl((-1.1, 0.25, 1.0), 0.05, 0.5, axis="y", segments=12), shiny("F2B632"))
    m.add(lathe([(0.12, 0.0), (0.25, 0.0), (0.18, 0.25), (0.08, 0.25)], segments=24), col("F3E9D2"), transform=Matrix.Translation((-1.1, 0.5, 1.0)))
    m.add(cyl((-1.1, 0.02, 1.0), 0.14, 0.04, axis="y", segments=20), shiny("F2B632"))
    return [m.build()]


ALL = {
    "truck_cab": truck_cab, "truck_bed": truck_bed, "truck_wheel": truck_wheel, "parcel_stack": parcel_stack,
    "road_tile": road_tile, "speedbump": speedbump2, "pothole": pothole, "cobbles": cobbles,
    "tree_round": tree_round, "tree_pine": tree_pine, "bush": bush, "house_0": lambda: house(0), "house_1": lambda: house(1),
    "house_2": lambda: house(2), "fence": fence, "lamppost": lamppost, "mailbox": mailbox, "hill": hill, "cloud": cloud,
    "conveyor": conveyor, "bin": depot_bin, "chute": chute, "bumper": bumper, "robot_arm": robot_arm, "rack": rack,
    "hanglamp": hanging_lamp, "depot_wall": depot_wall, "depot_floor": depot_floor,
    "step": step, "porch": porch, "courier": courier, "street_tile": street_tile,
    "ship_deck": ship_deck, "waves": waves, "plane_hold": plane_hold, "catapult": catapult, "haystack": haystack,
    "tower": tower, "reveal_room": reveal_room,
}
