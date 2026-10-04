"""Procedural textures (numpy) -> Assets/Resources/Textures/*.png.

    blender -b -P ArtSource/make_textures.py [-- name1 name2 ...]

Text (stamps, labels) is rasterised by rendering Blender text objects with the game's own fonts.
"""
import math
import os
import sys

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Textures")
FONTS = os.path.join(ROOT, "Assets", "Resources", "Fonts")
rng = np.random.default_rng(1887)


# ----------------------------------------------------------------------------- noise helpers

def value_noise(h, w, cells_y, cells_x, seed=0, tile=True):
    """Smooth tileable value noise in [0,1]."""
    r = np.random.default_rng(seed)
    grid = r.random((cells_y + 1, cells_x + 1))
    if tile:
        grid[-1, :] = grid[0, :]
        grid[:, -1] = grid[:, 0]
    ys = np.linspace(0, cells_y, h, endpoint=False)
    xs = np.linspace(0, cells_x, w, endpoint=False)
    y0 = np.floor(ys).astype(int)
    x0 = np.floor(xs).astype(int)
    fy = ys - y0
    fx = xs - x0
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    a = grid[y0][:, x0]
    b = grid[y0][:, x0 + 1]
    c = grid[y0 + 1][:, x0]
    d = grid[y0 + 1][:, x0 + 1]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def fbm(h, w, base=4, octaves=5, seed=0, aspect=(1, 1)):
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cy = max(1, int(base * aspect[0] * 2 ** o))
        cx = max(1, int(base * aspect[1] * 2 ** o))
        out += value_noise(h, w, cy, cx, seed + o * 31) * amp
        total += amp
        amp *= 0.5
    return out / total


def blur(img, r=2):
    """Cheap separable box blur (wraps)."""
    out = img.copy()
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=axis)
        out = acc / (2 * r + 1)
    return out


def normal_from_height(hgt, strength=2.0):
    dx = (np.roll(hgt, -1, axis=1) - np.roll(hgt, 1, axis=1)) * strength
    dy = (np.roll(hgt, -1, axis=0) - np.roll(hgt, 1, axis=0)) * strength
    n = np.dstack([-dx, -dy, np.ones_like(hgt)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return (n * 0.5 + 0.5)


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])


def save(name, rgb, alpha=None):
    """rgb: (h, w, 3) float sRGB 0..1 (row 0 = top)."""
    h, w = rgb.shape[:2]
    a = np.ones((h, w)) if alpha is None else alpha
    px = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])[::-1]  # Blender images are bottom-up
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels = px.astype(np.float32).ravel()
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"[tex] {name} {w}x{h}")


def save_normal(name, n):
    h, w = n.shape[:2]
    px = np.dstack([n, np.ones((h, w))])[::-1]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels = px.astype(np.float32).ravel()
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"[tex] {name} (normal)")


# ----------------------------------------------------------------------------- text rendering

_text_scene_ready = False


def text_mask(text, font="FiraSansCompressed-Heavy", width=1024, height=256, size=1.0, align="CENTER", letter_spacing=0.0):
    """Renders `text` white-on-black and returns an alpha mask (h, w) in [0,1]."""
    scene = bpy.context.scene
    for o in list(scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    fpath = os.path.join(FONTS, font + ".ttf")
    vf = bpy.data.fonts.load(fpath, check_existing=True)
    cu = bpy.data.curves.new("txt", "FONT")
    cu.body = text
    cu.font = vf
    cu.align_x = align
    cu.align_y = "CENTER"
    cu.size = size
    cu.space_character = 1.0 + letter_spacing
    ob = bpy.data.objects.new("txt", cu)
    scene.collection.objects.link(ob)
    mat = bpy.data.materials.get("txtmat") or bpy.data.materials.new("txtmat")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (1, 1, 1, 1)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    cu.materials.append(mat)
    cam_d = bpy.data.cameras.new("tcam")
    cam_d.type = "ORTHO"
    cam = bpy.data.objects.new("tcam", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (0, 0, 10)
    # fit text width into the frame
    bpy.context.view_layer.update()
    dims = ob.dimensions
    aspect = width / height
    scale = max(dims.x / aspect, dims.y) * 1.08
    cam_d.ortho_scale = max(scale, 0.01) * aspect if aspect >= 1 else max(scale, 0.01)
    bb = [ob.matrix_world @ __import__("mathutils").Vector(c) for c in ob.bound_box]
    cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
    cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
    cam.location = (cx, cy, 10)
    scene.render.engine = "BLENDER_WORKBENCH" if False else scene.render.engine
    eng_ids = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in eng_ids else "BLENDER_EEVEE"
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.get("tw") or bpy.data.worlds.new("tw")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)
    scene.world = world
    path = "/tmp/hwc_text_render.png"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:]).reshape(height, width, 4)[::-1]
    bpy.data.images.remove(img)
    return np.clip(px[..., 0], 0, 1)


def paste(dst_rgb, dst_a, mask, color, x, y, opacity=1.0):
    """Composite a mask (h, w) of `color` at (x, y) top-left into dst."""
    h, w = mask.shape
    H, W = dst_rgb.shape[:2]
    x1, y1 = min(W, x + w), min(H, y + h)
    m = mask[: y1 - y, : x1 - x] * opacity
    dst_rgb[y:y1, x:x1] = dst_rgb[y:y1, x:x1] * (1 - m[..., None]) + np.asarray(color)[None, None, :] * m[..., None]
    if dst_a is not None:
        dst_a[y:y1, x:x1] = np.maximum(dst_a[y:y1, x:x1], m)


def resize_mask(mask, w, h):
    ys = (np.arange(h) * mask.shape[0] / h).astype(int)
    xs = (np.arange(w) * mask.shape[1] / w).astype(int)
    return mask[ys][:, xs]


def ink_wear(shape, seed, amount=0.35):
    """Distressed rubber-stamp ink coverage (1 = full ink)."""
    n = fbm(shape[0], shape[1], base=6, octaves=4, seed=seed)
    speck = rng.random(shape)
    cov = np.clip((n - 0.25) * 3.0, 0, 1)
    cov = cov * (speck > amount * 0.25)
    return np.clip(0.55 + cov * 0.45, 0, 1)


# ----------------------------------------------------------------------------- textures

def kraft_base(n, color, seed, streak=1.0):
    fib = fbm(n, n, base=3, octaves=6, seed=seed)
    streaks = fbm(n, n, base=2, octaves=5, seed=seed + 7, aspect=(6, 0.5))
    fine = rng.random((n, n))
    fine = blur(fine, 1)
    lum = 0.88 + 0.12 * fib + 0.07 * (streaks - 0.5) * streak + 0.05 * (fine - 0.5)
    # sparse darker fibres
    flecks = (rng.random((n, n)) > 0.9993).astype(float)
    flecks = np.maximum(flecks, np.roll(flecks, 1, axis=1) * 0.8)
    flecks = np.maximum(flecks, np.roll(flecks, 2, axis=1) * 0.5)
    lum -= blur(flecks, 1) * 0.35
    hair = fbm(n, n, base=8, octaves=3, seed=seed + 3, aspect=(16, 0.4))
    lum += (hair - 0.5) * 0.05
    rgb = hexrgb(color)[None, None, :] * lum[..., None]
    height = 0.6 * fib + 0.3 * streaks + 0.1 * fine
    return rgb, height


def make_kraft():
    rgb, hgt = kraft_base(1024, "C8955A", 11)
    save("kraft", rgb)
    save_normal("kraft_n", normal_from_height(hgt, 3.0))


def make_kraftin():
    rgb, hgt = kraft_base(1024, "D9AE78", 23, streak=0.6)
    save("kraftin", rgb)
    save_normal("kraftin_n", normal_from_height(hgt, 2.0))


def make_corrugate():
    w, h = 512, 128
    y = np.linspace(0, 1, h)[:, None] * np.ones((1, w))
    x = np.linspace(0, 1, w)[None, :] * np.ones((h, 1))
    base = hexrgb("B98A55")
    rgb = np.ones((h, w, 3)) * base * 0.75
    wave = 0.5 + 0.3 * np.sin(x * 2 * math.pi * 10)
    flute = np.abs(y - wave) < 0.06
    liner = (y < 0.14) | (y > 0.86)
    rgb[flute] = hexrgb("D2A26B")
    rgb[liner] = hexrgb("C8955A")
    shade = 0.85 + 0.15 * np.sin(x * 2 * math.pi * 10 + 1.2)
    rgb[~(flute | liner)] *= shade[~(flute | liner)][:, None] * 0.7
    save("corrugate", rgb)


def tape(name, base, pattern=None, seed=0):
    w, h = 1024, 128
    lum = 0.93 + 0.07 * fbm(h, w, base=2, octaves=4, seed=seed, aspect=(1, 12))
    rgb = hexrgb(base)[None, None, :] * lum[..., None]
    edge = np.ones((h, w))
    edge[:6, :] = 0.82
    edge[-6:, :] = 0.82
    rgb *= edge[..., None]
    if pattern == "stripes":
        xs = np.arange(w)[None, :] + np.arange(h)[:, None]
        rgb[(xs // 48) % 2 == 0] = rgb[(xs // 48) % 2 == 0] * 0.3 + hexrgb("D9483B") * 0.7
    elif pattern == "polka":
        yy, xx = np.mgrid[0:h, 0:w]
        d = ((xx % 64) - 32) ** 2 + ((yy % 64) - 32) ** 2
        rgb[d < 14 ** 2] = hexrgb("F6EFDD")
    elif pattern == "fragile":
        m = text_mask("FRAGILE  •  HANDLE WITH CARE  •  ", width=1024, height=128)
        paste(rgb, None, m * 0.95, hexrgb("C8302A"), 0, 0)
    elif pattern == "scales":
        yy, xx = np.mgrid[0:h, 0:w]
        row = (yy // 32)
        cx = (xx + (row % 2) * 32) % 64 - 32
        cy = yy % 32
        d = np.sqrt(cx ** 2 + (cy - 32) ** 2)
        ring = (d > 26) & (d < 31)
        rgb[ring] = rgb[ring] * 0.55
    elif pattern == "gold":
        shine = 0.75 + 0.35 * np.sin(np.arange(w)[None, :] / w * math.pi * 6 + np.arange(h)[:, None] / h)
        rgb = hexrgb("E8B932")[None, None, :] * shine[..., None]
    save(name, rgb)


def make_tapes():
    tape("tape_kraft", "D9B26F", seed=3)
    tape("tape_stripe", "F3E9D2", "stripes", seed=4)
    tape("tape_polka", "2F8F8B", "polka", seed=5)
    tape("tape_fragile", "F3E9D2", "fragile", seed=6)
    tape("tape_scales", "8E3B6B", "scales", seed=7)
    tape("tape_gold", "E8B932", "gold", seed=8)


def make_label():
    w, h = 768, 512
    rgb = np.ones((h, w, 3)) * hexrgb("F7F0DE")
    rgb *= (0.96 + 0.04 * fbm(h, w, base=4, octaves=4, seed=9))[..., None]
    a = np.ones((h, w))
    rgb[:110, :] = hexrgb("D9483B")
    m = text_mask("MOSSBURY PARCEL POST", width=700, height=90)
    paste(rgb, None, m, hexrgb("F7F0DE"), 34, 10)
    ink = hexrgb("1F2A44")
    for i, line in enumerate(("TO:", "FROM:  MABEL, PACKING DEPT.")):
        m = text_mask(line, font="FiraSans-Bold", width=700, height=56, align="LEFT")
        paste(rgb, None, m, ink, 30, 135 + i * 70)
    for i in range(3):
        rgb[290 + i * 40: 293 + i * 40, 30:470] = ink * 0.8 + 0.2
    # barcode
    xs = 520
    r = np.random.default_rng(42)
    while xs < 740:
        bw = r.integers(2, 7)
        if r.random() > 0.35:
            rgb[300:470, xs:xs + bw] = ink
        xs += bw + r.integers(2, 5)
    m = text_mask("HANDLE WITH CARE", width=420, height=60)
    paste(rgb, None, m * ink_wear(m.shape, 3), hexrgb("C8302A"), 40, 420)
    rgb[:4, :] *= 0.7
    rgb[-4:, :] *= 0.7
    rgb[:, :4] *= 0.7
    rgb[:, -4:] *= 0.7
    save("label", rgb, a)


def round_stamp(name, top, bottom, center, color, seed):
    n = 512
    c = n / 2
    yy, xx = np.mgrid[0:n, 0:n]
    d = np.sqrt((xx - c) ** 2 + (yy - c) ** 2)
    ring = ((d > 228) & (d < 246)) | ((d > 168) & (d < 178))
    alpha = ring.astype(float)
    rgb = np.ones((n, n, 3)) * hexrgb(color)
    phi = np.arctan2(c - yy, xx - c)          # maths angle, y up
    span = math.pi * 0.9
    r_in, r_out = 182.0, 226.0
    band = (d >= r_in) & (d < r_out)
    for txt, is_top in ((top, True), (bottom, False)):
        m = text_mask(txt, width=1400, height=110, letter_spacing=0.15)
        hh, ww = m.shape
        if is_top:
            u = ((math.pi / 2 + span / 2) - phi) / span
            v = (r_out - d) / (r_out - r_in)       # letter tops point outward
            sel = band & (phi > 0)
        else:
            u = (phi - (-math.pi / 2 - span / 2)) / span
            v = (d - r_in) / (r_out - r_in)        # letter tops point inward
            sel = band & (phi < 0)
        sel = sel & (u >= 0) & (u < 1)
        ui = np.clip((u * ww).astype(int), 0, ww - 1)
        vi = np.clip((v * hh).astype(int), 0, hh - 1)
        alpha[sel] = np.maximum(alpha[sel], m[vi[sel], ui[sel]])
    m = text_mask(center, width=300, height=120)
    paste(rgb, alpha, m, hexrgb(color), 106, 196)
    alpha *= ink_wear((n, n), seed)
    save(name, rgb, alpha)


def make_decals():
    round_stamp("decal_logo", "MOSSBURY PARCEL POST", "WE SHIP ANYTHING", "EST. 1887", "C8302A", 5)
    # THIS WAY UP arrows
    n = 512
    rgb = np.ones((n, n, 3)) * hexrgb("1F2A44")
    alpha = np.zeros((n, n))
    yy, xx = np.mgrid[0:n, 0:n]
    for cx in (170, 342):
        shaft = (np.abs(xx - cx) < 22) & (yy > 180) & (yy < 380)
        head = (yy > 70) & (yy <= 190) & (np.abs(xx - cx) < (yy - 70) * 0.62)
        alpha[shaft | head] = 1
    alpha[400:418, 120:392] = 1
    m = text_mask("THIS WAY UP", width=440, height=80)
    paste(rgb, alpha, m, hexrgb("1F2A44"), 36, 425)
    alpha *= ink_wear((n, n), 8, 0.2)
    save("decal_arrows", rgb, alpha)
    # FRAGILE stamp with a glass
    w, h = 768, 320
    rgb = np.ones((h, w, 3)) * hexrgb("C8302A")
    alpha = np.zeros((h, w))
    alpha[8:24, 8:w - 8] = 1
    alpha[h - 24:h - 8, 8:w - 8] = 1
    alpha[8:h - 8, 8:24] = 1
    alpha[8:h - 8, w - 24:w - 8] = 1
    m = text_mask("FRAGILE", width=540, height=220)
    paste(rgb, alpha, m, hexrgb("C8302A"), 200, 50)
    yy, xx = np.mgrid[0:h, 0:w]
    bowl = (((xx - 110) / 52.0) ** 2 + ((yy - 110) / 60.0) ** 2 < 1) & (yy < 140)
    stem = (np.abs(xx - 110) < 8) & (yy >= 140) & (yy < 230)
    foot = (np.abs(xx - 110) < 40) & (yy >= 225) & (yy < 245)
    alpha[bowl | stem | foot] = 1
    alpha *= ink_wear((h, w), 12, 0.2)
    save("decal_fragile", rgb, alpha)


def make_wood():
    n = 1024
    plank_h = n // 4
    rgb = np.zeros((n, n, 3))
    hgt = np.zeros((n, n))
    for p in range(4):
        seed = 100 + p
        grain = fbm(plank_h, n, base=2, octaves=6, seed=seed, aspect=(1, 0.15))
        rings = np.sin((grain * 18 + np.linspace(0, 3, n)[None, :] * 0.5) * math.pi)
        lum = 0.82 + 0.1 * grain + 0.06 * rings
        tint = hexrgb(["9C6B43", "A47448", "946540", "A06F45"][p])
        rgb[p * plank_h:(p + 1) * plank_h] = tint * lum[..., None]
        hgt[p * plank_h:(p + 1) * plank_h] = grain * 0.5 + rings * 0.1
        rgb[p * plank_h:p * plank_h + 4] *= 0.55
        hgt[p * plank_h:p * plank_h + 4] -= 0.6
    save("wood", rgb)
    save_normal("wood_n", normal_from_height(hgt, 2.5))


def make_plaster():
    n = 512
    f = fbm(n, n, base=3, octaves=6, seed=77)
    rgb = hexrgb("E9D8BC")[None, None, :] * (0.93 + 0.08 * f)[..., None]
    save("plaster", rgb)
    save_normal("plaster_n", normal_from_height(f, 2.0))


def make_pegboard():
    n = 512
    rgb = hexrgb("A9BFAF")[None, None, :] * (0.93 + 0.07 * fbm(n, n, base=4, octaves=4, seed=88))[..., None]
    yy, xx = np.mgrid[0:n, 0:n]
    d = ((xx % 64) - 32) ** 2 + ((yy % 64) - 32) ** 2
    hgt = np.zeros((n, n))
    rgb[d < 7 ** 2] = hexrgb("2E3A34")
    hgt[d < 8 ** 2] = -1
    save("pegboard", rgb)
    save_normal("pegboard_n", normal_from_height(blur(hgt, 1), 3.0))


def make_paper_ui():
    n = 512
    f = fbm(n, n, base=4, octaves=6, seed=55)
    fib = blur(rng.random((n, n)), 1)
    rgb = hexrgb("F3E9D2")[None, None, :] * (0.95 + 0.05 * f + 0.03 * (fib - 0.5))[..., None]
    save("ui_paper", rgb)


ALL = {
    "kraft": make_kraft, "kraftin": make_kraftin, "corrugate": make_corrugate, "tapes": make_tapes,
    "label": make_label, "decals": make_decals, "wood": make_wood, "plaster": make_plaster,
    "pegboard": make_pegboard, "paper_ui": make_paper_ui,
}


def main():
    os.makedirs(OUT, exist_ok=True)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name, fn in ALL.items():
        if argv and name not in argv:
            continue
        fn()


main()
