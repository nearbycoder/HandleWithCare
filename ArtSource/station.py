"""The packing station (bench, wall, props) and the pigeonhole item shelves.

Station origin: bench top surface at y = 0, the box sits centred at x = 0, wall at z ~ 0.95.
Item shelves: origin at the bottom-left-front corner of the shelf interior, slot = 0.62 m.
"""
import math

from mathutils import Matrix

from hwc_lib import (Model, V, bezier, box, cbox, col, cyl, ellipsoid, glass, glow, hull, lathe, matte, metal,
                     prism, rod, rotate_about, shade, shell, shiny, sphere, sweep, tex, torus)

SLOT = 0.62


def station():
    s = Model("station")
    wood, plaster, peg = tex("wood"), tex("plaster"), tex("pegboard")
    # bench
    s.add(box((-4.2, -0.09, -0.75), (4.6, 0.0, 0.8), bevel=0.012), wood, uv_scale=0.9)
    s.add(box((-4.2, -0.16, -0.78), (4.6, -0.07, -0.7), bevel=0.01), col("6E4528"))
    s.add(box((-4.2, -1.3, -0.62), (4.6, -0.16, -0.58)), col("5A3A22"))
    # floor far below (rarely seen)
    s.add(box((-8, -1.42, -4), (8, -1.3, 4)), col("4A3A30"))
    # back wall: plaster above, wainscot below
    s.add(box((-8, -1.3, 0.95), (8, 3.8, 1.05)), plaster, uv_scale=0.8)
    s.add(box((-8, -1.3, 0.93), (8, 0.25, 0.95)), col("2F5E5A"))
    s.add(box((-8, 0.23, 0.92), (8, 0.29, 0.95), bevel=0.008), col("F3E9D2"))
    # pegboard panel
    s.add(box((-2.6, 0.45, 0.91), (2.9, 1.85, 0.93), bevel=0.006), peg, uv_scale=2.0)
    s.add(box((-2.65, 0.4, 0.89), (2.95, 0.46, 0.94), bevel=0.006), col("F3E9D2"))
    s.add(box((-2.65, 1.84, 0.89), (2.95, 1.9, 0.94), bevel=0.006), col("F3E9D2"))
    # tools on the pegboard
    red, steel, blk = shiny("D9483B"), metal("C9CED4"), col("2A2628")
    # scissors
    for sgn in (-1, 1):
        s.add(torus((-2.1 + sgn * 0.035, 1.2, 0.88), 0.03, 0.007, axis="z", segments=18, ring_segments=6), red)
    s.add(hull([(-2.12, 1.25, 0.885), (-2.08, 1.25, 0.885), (-2.1, 1.55, 0.885), (-2.1, 1.55, 0.875)]), steel)
    # ruler
    s.add(box((-1.85, 0.7, 0.88), (-1.79, 1.6, 0.895), bevel=0.002), col("E8C47A"))
    for k in range(9):
        s.add(box((-1.85, 0.75 + k * 0.1, 0.879), (-1.82, 0.752 + k * 0.1, 0.881)), blk)
    # tape gun
    s.add(cyl((2.2, 1.35, 0.85), 0.07, 0.07, axis="z", segments=24), col("D9B26F"))
    s.add(cyl((2.2, 1.35, 0.85), 0.03, 0.075, axis="z", segments=16), col("4A3424"))
    s.add(box((2.13, 1.22, 0.83), (2.32, 1.29, 0.87), bevel=0.01), red)
    s.add(box((2.12, 1.08, 0.83), (2.17, 1.25, 0.87), bevel=0.01), red)
    # string spool and hook
    s.add(cyl((1.75, 1.5, 0.86), 0.05, 0.08, axis="y", segments=20), col("E8DCC0"))
    s.add(cyl((1.75, 1.5, 0.86), 0.055, 0.012, axis="y", segments=20), col("6E4528"), transform=Matrix.Translation((0, 0.04, 0)))
    # clipboard with an order sheet
    s.add(box((-2.45, 0.55, 0.88), (-2.05, 1.05, 0.895), bevel=0.006), col("9C6B43"))
    s.add(box((-2.42, 0.58, 0.875), (-2.08, 0.98, 0.88)), matte("F7F0DE"))
    s.add(box((-2.3, 1.0, 0.87), (-2.2, 1.06, 0.885), bevel=0.004), steel)
    # sign
    s.add(box((-1.0, 2.05, 0.9), (1.0, 2.5, 0.94), bevel=0.02), shiny("D9483B"))
    s.add(box((-0.96, 2.09, 0.895), (0.96, 2.46, 0.9)), col("F3E9D2"))
    s.add(box((-0.9, 2.15, 0.893), (0.9, 2.4, 0.896)), shiny("1F2A44"))
    # wall shelf with flat-packed boxes, jars, a radio and a plant
    s.add(box((-4.0, 2.0, 0.6), (-1.4, 2.04, 0.93), bevel=0.006), tex("wood"), uv_scale=1.0)
    s.add(box((1.4, 2.0, 0.6), (4.2, 2.04, 0.93), bevel=0.006), tex("wood"), uv_scale=1.0)
    for k in range(6):
        s.add(box((-3.8 + k * 0.012, 2.04, 0.65), (-3.0 + k * 0.012, 2.06 + k * 0.022, 0.9)), tex("kraft"), uv_scale=2.0,
              transform=rotate_about((-3.4, 2.05, 0.77), "Y", k * 3))
    for k, c in enumerate(("D9483B", "2F8F8B", "F2B632")):
        s.add(lathe([(0.0, 0), (0.05, 0), (0.055, 0.1), (0.04, 0.12), (0.0, 0.12)], segments=16), glass(shade(c, 1.2)),
              transform=Matrix.Translation((-2.6 + k * 0.16, 2.04, 0.78)))
        s.add(cyl((-2.6 + k * 0.16, 2.17, 0.78), 0.04, 0.03, axis="y", segments=16), shiny(c))
    s.add(box((1.7, 2.04, 0.65), (2.25, 2.34, 0.9), bevel=0.03), shiny("2F8F8B"))
    s.add(cyl((1.86, 2.18, 0.645), 0.08, 0.01, axis="z", segments=24), col("E8DCC0"))
    s.add(box((2.0, 2.12, 0.64), (2.2, 2.27, 0.65)), col("E8DCC0"))
    s.add(rod((2.18, 2.34, 0.8), (2.1, 2.55, 0.8), 0.006), steel)
    s.add(lathe([(0.0, 0), (0.09, 0), (0.11, 0.16), (0.0, 0.16)], segments=20), col("C8693F"), transform=Matrix.Translation((3.3, 2.04, 0.78)))
    for k in range(7):
        a = k / 7 * 2 * math.pi
        tip = V(3.3 + math.cos(a) * 0.22, 2.4 + (k % 3) * 0.06, 0.78 + math.sin(a) * 0.12)
        s.add(sweep(bezier(V(3.3, 2.18, 0.78), V(3.3, 2.32, 0.78), tip + V(0, 0.08, 0), tip, 8), 0.022, radius_end=0.006,
                    profile_scale=(1, 0.35)), col("5E9E4A"))
    # window (left), with a warm daylight glow
    s.add(box((-5.4, 0.7, 0.9), (-3.6, 2.4, 0.96), bevel=0.02), col("F3E9D2"))
    s.add(box((-5.3, 0.8, 0.92), (-3.7, 2.3, 0.95)), glow("FFE9C2"))
    s.add(box((-4.52, 0.8, 0.9), (-4.48, 2.3, 0.94)), col("F3E9D2"))
    s.add(box((-5.3, 1.53, 0.9), (-3.7, 1.57, 0.94)), col("F3E9D2"))
    s.add(box((-5.5, 0.62, 0.7), (-3.5, 0.7, 0.98), bevel=0.01), col("F3E9D2"))
    # desk lamp (left, behind)
    lamp_base = V(-2.05, 0.0, 0.6)
    s.add(cyl(lamp_base + V(0, 0.015, 0), 0.12, 0.03, axis="y", segments=28, bevel=0.008), shiny("2F8F8B"))
    s.add(rod(lamp_base + V(0, 0.03, 0), lamp_base + V(0.1, 0.62, -0.05), 0.014), steel)
    s.add(rod(lamp_base + V(0.1, 0.62, -0.05), lamp_base + V(0.45, 0.78, -0.25), 0.014), steel)
    s.add(sphere(lamp_base + V(0.1, 0.62, -0.05), 0.026), shiny("2F8F8B"))
    head = lamp_base + V(0.5, 0.74, -0.28)
    s.add(lathe([(0.03, 0.0), (0.06, 0.02), (0.13, 0.15), (0.135, 0.16)], segments=28), shiny("2F8F8B"),
          transform=Matrix.Translation(head) @ Matrix.Rotation(math.radians(150), 4, "Z") @ Matrix.Rotation(math.radians(-25), 4, "X"))
    s.add(sphere(head + V(0.06, -0.1, -0.02), 0.045), glow("FFE2B0"))
    # bench props (left side)
    s.add(cyl((-1.75, 0.075, -0.15), 0.12, 0.15, axis="y", segments=28), glass("CFE8F5"))  # bubble wrap roll on its end
    s.add(cyl((-1.75, 0.076, -0.15), 0.035, 0.152, axis="y", segments=16), col("C8955A"))
    s.add(box((-2.35, 0.0, -0.4), (-1.98, 0.06, -0.12), bevel=0.004), matte("F3EDE0"))
    s.add(box((-2.33, 0.06, -0.38), (-2.0, 0.066, -0.14)), matte("F7F2E6"), transform=rotate_about((-2.15, 0.06, -0.26), "Y", 8))
    # rubber stamp and ink pad
    s.add(box((-2.45, 0.0, 0.1), (-2.2, 0.03, 0.28), bevel=0.006), shiny("1F2A44"))
    s.add(box((-2.43, 0.03, 0.12), (-2.22, 0.033, 0.26)), col("A8322A"))
    s.add(cyl((-2.0, 0.03, 0.2), 0.045, 0.06, axis="y", segments=20), col("A8322A"))
    s.add(lathe([(0.0, 0), (0.02, 0), (0.028, 0.06), (0.035, 0.1), (0.0, 0.12)], segments=16), shiny("9C6B43"), transform=Matrix.Translation((-2.0, 0.06, 0.2)))
    # pencil cup
    s.add(shell([(0.05, 0), (0.05, 0.14)], thickness=0.006), shiny("F2B632"), transform=Matrix.Translation((-2.6, 0.0, 0.5)))
    for k, c in enumerate(("D9483B", "2F8F8B", "1F2A44")):
        s.add(rod((-2.6 + (k - 1) * 0.02, 0.02, 0.5), (-2.6 + (k - 1) * 0.04, 0.24, 0.5 + (k - 1) * 0.02), 0.007), shiny(c))
    return [s.build()]


def itemshelf(cols, rows):
    m = Model(f"itemshelf_{cols}x{rows}")
    wood, dark = tex("wood"), col("6E4528")
    W, Hh = cols * SLOT, rows * SLOT
    d0, d1 = -0.21, 0.21
    for r in range(rows + 1):
        m.add(box((-0.03, r * SLOT - 0.035, d0), (W + 0.03, r * SLOT, d1), bevel=0.006), wood, uv_scale=1.2)
        m.add(box((-0.03, r * SLOT - 0.035, d0 - 0.012), (W + 0.03, r * SLOT, d0), bevel=0.004), dark)
    for c in range(cols + 1):
        m.add(box((c * SLOT - 0.03, -0.035, d0), (c * SLOT + 0.0, Hh, d1), bevel=0.006), wood, uv_scale=1.2)
    m.add(box((-0.03, -0.035, d1), (W + 0.03, Hh, d1 + 0.02)), dark)
    # little brass label holders under each slot
    for r in range(rows):
        for c in range(cols):
            m.add(box((c * SLOT + SLOT / 2 - 0.06, r * SLOT - 0.03, d0 - 0.016), (c * SLOT + SLOT / 2 + 0.06, r * SLOT - 0.004, d0 - 0.012), bevel=0.002), metal("E0B54A"))
    return [m.build()]


ALL = {"station": station}
for _c, _r in ((1, 1), (1, 2), (1, 3), (2, 1), (2, 2), (2, 3)):
    ALL[f"itemshelf_{_c}x{_r}"] = (lambda c=_c, r=_r: itemshelf(c, r))
