"""Cardboard boxes, one model per interior size (W x H cells). Cutaway front (open toward -z).

Hierarchy (names are looked up by BoxView): box_WxH (shell) -> FlapL, FlapR, FlapB (hinged at
their pivots), Tape (pivot at the left end; scaled along x to animate taping), Label.
"""
from hwc_lib import CELL, Model, V, box, col, decal, tex

DEPTH = 0.32
T = 0.022


def build_box(W, H):
    iw, ih, d, t = W * CELL, H * CELL, DEPTH, T
    x0, x1 = -iw / 2, iw / 2
    yb, yt = -ih / 2, ih / 2
    zf, zb = -d / 2, d / 2
    s = Model(f"box_{W}x{H}")
    kraft, inner, edge = tex("kraft"), tex("kraftin"), tex("corrugate")
    # outer shell
    s.add(box((x0 - t, yb - t, zf), (x1 + t, yb, zb + t), bevel=0.003), kraft, uv_scale=2.0)          # floor
    s.add(box((x0 - t, yb - t, zb), (x1 + t, yt, zb + t), bevel=0.003), kraft, uv_scale=2.0)          # back
    s.add(box((x0 - t, yb - t, zf), (x0, yt, zb + t), bevel=0.003), kraft, uv_scale=2.0)              # left
    s.add(box((x1, yb - t, zf), (x1 + t, yt, zb + t), bevel=0.003), kraft, uv_scale=2.0)              # right
    # inner liners (lighter, clean kraft)
    e = 0.0012
    s.add(box((x0, yb, zf + 0.001), (x1, yb + e, zb)), inner, uv_scale=2.0)
    s.add(box((x0, yb, zb - e), (x1, yt, zb)), inner, uv_scale=2.0)
    s.add(box((x0, yb, zf + 0.001), (x0 + e, yt, zb)), inner, uv_scale=2.0)
    s.add(box((x1 - e, yb, zf + 0.001), (x1, yt, zb)), inner, uv_scale=2.0)
    # corrugated cut edges on the open front
    c = 0.0008
    s.add(box((x0 - t, yb - t, zf - c), (x1 + t, yb, zf)), edge, uv_scale=(4.0, 1 / t))
    s.add(box((x0 - t, yb, zf - c), (x0, yt, zf)), edge, uv_scale=(1 / t, 4.0))
    s.add(box((x1, yb, zf - c), (x1 + t, yt, zf)), edge, uv_scale=(1 / t, 4.0))
    # printed decals on the outside walls (visible from the 3/4 journey camera)
    ds = min(0.26, ih * 0.8)
    s.add(box((x1 + t, -ds / 2, -ds / 2 + 0.01), (x1 + t + 0.0008, ds / 2, ds / 2 + 0.01)), decal("logo"), uv_scale=1.0 / ds, uv_offset=(0.5 - 0.01 / ds, 0.5))
    s.add(box((x0 - t - 0.0008, -ds / 2, -ds / 2 + 0.01), (x0 - t, ds / 2, ds / 2 + 0.01)), decal("arrows"), uv_scale=1.0 / ds, uv_offset=(0.5 + 0.01 / ds, 0.5))
    shell = s.build()

    zc = (zf + zb + t) / 2
    zl = (zb + t) - zf
    # side flaps (on top), pivot on their outer top edges
    fl = Model("FlapL")
    fl.add(box((x0 - t, yt + t, zf), (0, yt + 2 * t, zb + t), bevel=0.003), kraft, uv_scale=2.0)
    fl.add(box((x0 - t + 0.04, yt + 2 * t, zc - 0.08), (x0 - t + 0.04 + 0.2, yt + 2 * t + 0.0008, zc + 0.04)), decal("fragile"), uv_scale=(1 / 0.2, 1 / 0.083), uv_offset=(-(x0 - t + 0.04) / 0.2 * 1, 0))
    flap_l = fl.build(origin=(x0 - t, yt + t, zc), parent=shell)
    fr = Model("FlapR")
    fr.add(box((0, yt + t, zf), (x1 + t, yt + 2 * t, zb + t), bevel=0.003), kraft, uv_scale=2.0)
    lw = min(0.2, iw * 0.4)
    lh = lw * 0.66
    fr.add(box((x1 + t - 0.03 - lw, yt + 2 * t, zc - lh / 2), (x1 + t - 0.03, yt + 2 * t + 0.0008, zc + lh / 2)), tex("label"),
           uv_scale=(1 / lw, 1 / lh), uv_offset=(-(x1 + t - 0.03 - lw) / lw, -(zc - lh / 2) / lh))
    flap_r = fr.build(origin=(x1 + t, yt + t, zc), parent=shell)
    fb = Model("FlapB")
    fb.add(box((x0, yt, zb + t - zl * 0.48), (x1, yt + t, zb + t), bevel=0.003), kraft, uv_scale=2.0)
    flap_b = fb.build(origin=(0, yt, zb + t), parent=shell)
    # tape across the closed side flaps, pivot at the left end
    tp = Model("Tape")
    tw = 0.07
    lx0, lx1 = x0 - t - 0.004, x1 + t + 0.004
    ytop = yt + 2 * t
    tp.add(box((lx0, ytop, zc - tw / 2), (lx1, ytop + 0.0025, zc + tw / 2)), tex("tape_kraft"), uv_scale=(1 / (tw * 8), 1 / tw), uv_offset=(0, 0.5))
    tp.add(box((lx0 - 0.0025, ytop - 0.07, zc - tw / 2), (lx0, ytop + 0.0025, zc + tw / 2)), tex("tape_kraft"), uv_scale=(1 / (tw * 8), 1 / tw))
    tp.add(box((lx1, ytop - 0.07, zc - tw / 2), (lx1 + 0.0025, ytop + 0.0025, zc + tw / 2)), tex("tape_kraft"), uv_scale=(1 / (tw * 8), 1 / tw))
    tape = tp.build(origin=(lx0, ytop, zc), parent=shell)
    # optional front panel (hidden in play; the title screen shows a closed parcel)
    fp = Model("Front")
    fp.add(box((x0 - t, yb - t, zf - t), (x1 + t, yt, zf), bevel=0.003), kraft, uv_scale=2.0)
    ds2 = min(0.3, ih * 0.75, iw * 0.5)
    fp.add(box((-ds2 / 2, -ds2 / 2, zf - t - 0.0008), (ds2 / 2, ds2 / 2, zf - t)), decal("logo"), uv_scale=1.0 / ds2, uv_offset=(0.5, 0.5))
    fp.add(box((x1 - 0.25, yb + 0.02, zf - t - 0.0009), (x1 - 0.03, yb + 0.02 + 0.092, zf - t)), decal("fragile"), uv_scale=(1 / 0.22, 1 / 0.092), uv_offset=(-(x1 - 0.25) / 0.22, -(yb + 0.02) / 0.092))
    fp.build(parent=shell)
    return [shell]


ALL = {}
for _w in range(3, 9):
    for _h in range(2, 6):
        ALL[f"{_w}x{_h}"] = (lambda w=_w, h=_h: build_box(w, h))
