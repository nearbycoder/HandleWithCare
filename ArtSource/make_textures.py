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


def save(name, rgb, alpha=None, out=None):
    """rgb: (h, w, 3) float sRGB 0..1 (row 0 = top)."""
    h, w = rgb.shape[:2]
    a = np.ones((h, w)) if alpha is None else alpha
    px = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])[::-1]  # Blender images are bottom-up
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels = px.astype(np.float32).ravel()
    img.filepath_raw = os.path.join(out or OUT, name + ".png")
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


# ----------------------------------------------------------------------------- PBR surfaces
# Tileable albedo + normal + mask (R metallic, G occlusion, A smoothness) for the big journey
# surfaces, where a per-model bake would be far too blurry.

def worley(n, cells, seed=0, jitter=0.9):
    """Tileable cellular noise on an n x n image: (F1, F2, cell id), distances in cell units."""
    r = np.random.default_rng(seed)
    px = (r.random((cells, cells)) - 0.5) * jitter + 0.5
    py = (r.random((cells, cells)) - 0.5) * jitter + 0.5
    ids = r.random((cells, cells))
    coord = (np.arange(n) + 0.5) / n * cells
    gy, gx = np.meshgrid(coord, coord, indexing="ij")
    cy, cx = np.floor(gy).astype(int), np.floor(gx).astype(int)
    f1 = np.full((n, n), 9.0)
    f2 = np.full((n, n), 9.0)
    cid = np.zeros((n, n))
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            ny, nx = cy + oy, cx + ox
            wy, wx = ny % cells, nx % cells
            d = np.hypot(gx - (nx + px[wy, wx]), gy - (ny + py[wy, wx]))
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            cid = np.where(closer, ids[wy, wx], cid)
            f1 = np.where(closer, d, f1)
    return f1, f2, cid


def save_mask(name, metal, ao, smooth):
    h, w = ao.shape
    px = np.dstack([np.broadcast_to(metal, (h, w)), ao, np.zeros((h, w)), np.broadcast_to(smooth, (h, w))])[::-1]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "CHANNEL_PACKED"
    img.pixels = px.astype(np.float32).ravel()
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"[tex] {name} (mask)")


def pbr_set(name, rgb, hgt, strength, metal, ao, smooth):
    save(name, rgb)
    save_normal(name + "_n", normal_from_height(hgt, strength))
    save_mask(name + "_mask", metal, np.clip(ao, 0, 1), np.clip(smooth, 0, 1))


def tint(hexcol, lum):
    return hexrgb(hexcol)[None, None, :] * lum[..., None]


def make_asphalt():
    n = 1024
    f1, f2, cid = worley(n, 160, seed=11)
    stones = np.clip(1 - f1 / 0.45, 0, 1) ** 0.7                    # rounded aggregate
    fine = blur(rng.random((n, n)), 1)
    patches = fbm(n, n, base=3, octaves=5, seed=12)
    lum = 0.30 + 0.12 * patches + 0.18 * stones * (cid - 0.3) + 0.06 * (fine - 0.5)
    rgb = tint("8E8A88", np.clip(lum, 0.12, 0.75) / 0.42)
    # tar seams and a few cracks
    crack = np.zeros((n, n))                                         # (hairline cracks read as doodles; left out)
    hgt = stones * 0.6 + fine * 0.25 - crack * 0.8
    pbr_set("asphalt", rgb, hgt, 3.0, 0.0, 1 - 0.35 * (1 - stones) * (f1 > 0.3) - 0.4 * crack, 0.12 + 0.12 * (1 - stones) + 0.08 * patches)


def make_concrete():
    n = 1024
    blot = fbm(n, n, base=3, octaves=6, seed=21)
    fine = fbm(n, n, base=64, octaves=3, seed=22)
    f1, _, _ = worley(n, 90, seed=23)
    pores = (f1 < 0.12).astype(float) * (rng.random((n, n)) > 0.4)
    pores = blur(pores, 1)
    lum = 0.86 + 0.12 * (blot - 0.5) + 0.06 * (fine - 0.5) - 0.25 * pores
    rgb = tint("B7B2AC", lum)
    hgt = blot * 0.3 + fine * 0.3 - pores * 0.8
    pbr_set("concrete", rgb, hgt, 2.5, 0.0, 1 - 0.5 * pores, 0.2 + 0.15 * blot)


def make_grass():
    n = 1024
    # blades: thin vertical streaks of random length and lean, overlapping in layers
    hgt = np.zeros((n, n))
    col = np.zeros((n, n, 3))
    base = tint("4F6E2E", 0.55 + 0.3 * fbm(n, n, base=4, octaves=5, seed=31))
    col[:] = base
    r = np.random.default_rng(32)
    yy = np.arange(n)
    for layer in range(3):
        count = 9000
        xs = r.integers(0, n, count)
        ys = r.integers(0, n, count)
        lens = r.integers(14, 38, count)
        leans = r.uniform(-0.35, 0.35, count)
        shades = r.uniform(0.55, 1.15, count) * (0.75 + 0.15 * layer)
        hues = r.integers(0, 4, count)
        palette = [hexrgb(h) for h in ("6E9A3A", "86AC48", "5D8A35", "A3B45A")]
        for k in range(count):
            for t in range(lens[k]):
                y = (ys[k] - t) % n
                x = int(xs[k] + leans[k] * t) % n
                w = 1.0 - t / lens[k]
                col[y, x] = palette[hues[k]] * shades[k] * (0.55 + 0.45 * (t / lens[k]))
                hgt[y, x] = layer * 0.3 + w * 0.3
    dirt = fbm(n, n, base=5, octaves=5, seed=33)
    soil = np.clip((dirt - 0.7) / 0.08, 0, 1) * blur(rng.random((n, n)), 2) * 1.6   # sparse, broken-up bare spots
    soil = np.clip(blur(soil, 2), 0, 1)
    col = col * (1 - 0.45 * soil[..., None]) + tint("6B5236", np.ones((n, n))) * (0.45 * soil[..., None])
    col *= (0.9 + 0.2 * fbm(n, n, base=2, octaves=3, seed=34))[..., None]                  # broad tonal variation
    hgt = blur(hgt, 1)
    pbr_set("grass", col, hgt, 2.5, 0.0, 0.55 + 0.45 * np.clip(hgt / 0.9, 0, 1), 0.08 + 0.1 * hgt)


def make_cobble():
    n = 1024
    f1, f2, cid = worley(n, 8, seed=41, jitter=0.6)
    edge = np.clip((f2 - f1) / 0.12, 0, 1)                         # 0 at the mortar joints
    dome = np.sqrt(np.clip(edge, 0, 1))
    grain = fbm(n, n, base=24, octaves=4, seed=42)
    tone = 0.75 + 0.3 * (cid - 0.5) + 0.12 * (grain - 0.5)
    rgb = tint("9A928B", tone) * (0.35 + 0.65 * edge ** 0.4)[..., None]
    joint = 1 - edge ** 0.25
    rgb = rgb * (1 - joint[..., None]) + tint("5A524A", 0.8 + 0.2 * grain) * joint[..., None]
    pbr_set("cobble", rgb, dome * 0.9 + grain * 0.15, 3.5, 0.0, 0.35 + 0.65 * edge ** 0.5, 0.15 + 0.25 * edge * grain)


def make_brick():
    n = 1024
    rows, cols = 16, 4
    yy, xx = np.mgrid[0:n, 0:n] / n
    row = np.floor(yy * rows).astype(int)
    off = (row % 2) * 0.5
    u = (xx * cols + off) % 1
    v = (yy * rows) % 1
    ids = np.floor(xx * cols + off).astype(int) % cols + row * cols
    r = np.random.default_rng(51)
    rnd = r.random(rows * cols + cols)[ids]
    mortar = np.minimum(np.minimum(u, 1 - u) * cols * 6, np.minimum(v, 1 - v) * rows * 1.5)
    brick = np.clip(mortar * 3, 0, 1)
    grain = fbm(n, n, base=32, octaves=4, seed=52)
    blot = fbm(n, n, base=6, octaves=4, seed=53)
    rgb = tint("A4553C", 0.75 + 0.35 * rnd + 0.12 * (grain - 0.5) + 0.1 * (blot - 0.5))
    rgb = rgb * brick[..., None] + tint("CFC4B4", 0.85 + 0.1 * grain) * (1 - brick[..., None])
    pbr_set("brick", rgb, brick * 0.8 + grain * 0.2, 3.0, 0.0, 0.5 + 0.5 * brick, 0.12 + 0.1 * grain)


def make_shingle():
    n = 1024
    rows, cols = 12, 6
    yy, xx = np.mgrid[0:n, 0:n] / n
    row = np.floor(yy * rows).astype(int)
    u = (xx * cols + (row % 2) * 0.5) % 1
    v = (yy * rows) % 1
    ids = (np.floor(xx * cols + (row % 2) * 0.5).astype(int) % cols) + row * cols
    rnd = np.random.default_rng(61).random(rows * cols + cols)[ids]
    gap = np.clip(np.minimum(u, 1 - u) * cols * 20, 0, 1)
    lip = v                                                         # each tile thickens toward its lower edge
    grain = fbm(n, n, base=40, octaves=4, seed=62)
    hgt = lip * 0.6 * gap + grain * 0.15
    lum = (0.75 + 0.3 * rnd + 0.1 * (grain - 0.5)) * (0.55 + 0.45 * gap) * (0.75 + 0.25 * lip)
    pbr_set("shingle", tint("8A8E96", lum), hgt, 3.0, 0.0, 0.5 + 0.5 * gap * lip, 0.15 + 0.1 * grain)


def make_treadplate():
    n = 512
    yy, xx = np.mgrid[0:n, 0:n] / n * 8
    def lug(ax, ay, ang):
        ca, sa = math.cos(ang), math.sin(ang)
        dx, dy = (xx - ax) % 1 - 0.5, (yy - ay) % 1 - 0.5
        u, v = dx * ca + dy * sa, -dx * sa + dy * ca
        return np.clip(1 - np.hypot(u / 0.32, v / 0.07), 0, 1) ** 0.5
    lugs = np.maximum(lug(0, 0, math.radians(45)), lug(0.5, 0.5, math.radians(-45)))
    scuff = fbm(n, n, base=8, octaves=6, seed=71)
    lum = 0.62 + 0.15 * (scuff - 0.5) + 0.1 * lugs
    pbr_set("treadplate", tint("B7BCC2", lum), lugs + scuff * 0.1, 4.0, 1.0, 0.7 + 0.3 * lugs, 0.45 + 0.25 * lugs - 0.15 * scuff)


def make_water():
    n = 1024
    swell = fbm(n, n, base=3, octaves=6, seed=81, aspect=(1, 0.5))
    ripple = fbm(n, n, base=24, octaves=4, seed=82, aspect=(1, 0.4))
    hgt = swell * 0.7 + ripple * 0.3
    foam = np.clip((ripple - 0.74) / 0.1, 0, 1) * np.clip((swell - 0.55) / 0.2, 0, 1) * 0.6
    rgb = tint("2F6E8C", 0.75 + 0.35 * swell) * (1 - foam[..., None]) + tint("E6F0F2", np.ones((n, n))) * foam[..., None]
    pbr_set("water", rgb, hgt, 6.0, 0.0, np.ones((n, n)), 0.92 - 0.5 * foam)


def make_hay():
    n = 512
    hgt = np.zeros((n, n))
    col = tint("B08A3A", 0.6 + 0.2 * fbm(n, n, base=4, octaves=4, seed=91))
    r = np.random.default_rng(92)
    pal = [hexrgb(h) for h in ("E8C46A", "D9B04A", "F0D68A", "C49A3C", "A9893F")]
    for k in range(14000):
        x0, y0 = r.uniform(0, n, 2)
        ang = r.uniform(-0.6, 0.6) + (math.pi / 2 if r.random() < 0.3 else 0)
        ln = r.integers(20, 60)
        c = pal[r.integers(0, 5)] * r.uniform(0.75, 1.1)
        for t in range(ln):
            x = int(x0 + math.cos(ang) * t) % n
            y = int(y0 + math.sin(ang) * t) % n
            col[y, x] = c
            hgt[y, x] = 0.5 + 0.5 * math.sin(t / ln * math.pi)
    hgt = blur(hgt, 1)
    pbr_set("hay", col, hgt, 3.0, 0.0, 0.45 + 0.55 * hgt, 0.15 + 0.15 * hgt)


def _foliage_canvas(n):
    return np.zeros((n, n, 3)), np.zeros((n, n)), np.zeros((n, n))   # colour, alpha, height


def _draw_leaf(col, alpha, hgt, cx, cy, ang, length, width, color, r):
    """Pointed oval leaf from (cx, cy) along `ang` (radians), with a midrib and soft dome."""
    n = alpha.shape[0]
    ca, sa = math.cos(ang), math.sin(ang)
    x0, x1 = int(max(0, min(cx, cx + ca * length) - width)), int(min(n, max(cx, cx + ca * length) + width + 1))
    y0, y1 = int(max(0, min(cy, cy + sa * length) - width)), int(min(n, max(cy, cy + sa * length) + width + 1))
    if x1 <= x0 or y1 <= y0:
        return
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
    dx, dy = xx - cx, yy - cy
    u = (dx * ca + dy * sa) / length                 # 0 at the stem, 1 at the tip
    v = (-dx * sa + dy * ca) / (width * 0.5)
    half = np.sin(np.clip(u, 0, 1) * math.pi) ** 0.75 * (1 - 0.25 * u)
    inside = (u > 0) & (u < 1) & (np.abs(v) < half)
    if not inside.any():
        return
    t = np.clip(np.abs(v) / np.maximum(half, 1e-3), 0, 1)
    dome = (1 - t ** 2) * 0.8 + 0.2
    rib = np.clip(1 - np.abs(v) * width * 0.5 / 1.6, 0, 1) * (u < 0.92)
    veins = 0.5 + 0.5 * np.sin((u * 9 - np.abs(v) * 2.2) * math.pi)
    shade = (0.82 + 0.18 * u) * (0.92 + 0.08 * veins) * (1 + 0.25 * rib) * r.uniform(0.85, 1.12)
    c = np.clip(color[None, None, :] * shade[..., None], 0, 1)
    sl = (slice(y0, y1), slice(x0, x1))
    m = inside
    col[sl][m] = c[m]
    alpha[sl][m] = 1.0
    hgt[sl][m] = (dome - 0.35 * rib + 0.05 * veins)[m] + r.uniform(0, 0.3)


def make_foliage_broadleaf():
    """Cluster of broad leaves on an alpha background, for tree and bush leaf cards."""
    n = 1024
    col, alpha, hgt = _foliage_canvas(n)
    r = np.random.default_rng(111)
    pal = [hexrgb(h) for h in ("4E7A2E", "5E8C34", "6A9A3A", "45702A", "7FA647", "3E6526")]
    for k in range(230):
        rad = r.random() ** 0.8 * n * 0.42
        a = r.uniform(0, 2 * math.pi)
        cx, cy = n / 2 + math.cos(a) * rad * 0.9, n / 2 + math.sin(a) * rad * 0.9
        ang = a + r.uniform(-0.8, 0.8)
        length = r.uniform(70, 125)
        _draw_leaf(col, alpha, hgt, cx, cy, ang, length, length * r.uniform(0.42, 0.55), pal[r.integers(0, len(pal))], r)
    # dilate colour into the transparent margin so filtered edges don't go dark
    for _ in range(6):
        grow = (alpha == 0) & (blur(alpha, 1) > 0)
        if not grow.any():
            break
        acc = blur(col * alpha[..., None], 1) / np.maximum(blur(alpha, 1), 1e-4)[..., None]
        col[grow] = acc[grow]
        alpha_tmp = alpha.copy()
        alpha_tmp[grow] = 1e-3
        alpha = alpha_tmp
    alpha = np.where(alpha >= 0.5, 1.0, 0.0)
    save("foliage_broadleaf", col, alpha)
    save_normal("foliage_broadleaf_n", normal_from_height(blur(hgt, 1), 2.5))


def make_foliage_needles():
    """Spray of conifer needles along a few twigs."""
    n = 1024
    col, alpha, hgt = _foliage_canvas(n)
    r = np.random.default_rng(222)
    pal = [hexrgb(h) for h in ("2F5A34", "3A6A3C", "28502E", "4A7A44")]
    for twig in range(14):
        a = twig / 14 * 2 * math.pi + r.uniform(-0.2, 0.2)
        sx, sy = n / 2 + math.cos(a) * 60, n / 2 + math.sin(a) * 60
        L = r.uniform(280, 400)
        ang = a + r.uniform(-0.3, 0.3)
        for t in np.linspace(0, 1, 70):
            px, py = sx + math.cos(ang) * L * t, sy + math.sin(ang) * L * t
            for side in (-1, 1):
                na = ang + side * r.uniform(0.6, 1.1)
                nl = r.uniform(55, 90) * (1 - 0.4 * t)
                c = pal[r.integers(0, len(pal))] * r.uniform(0.85, 1.15)
                for s in np.linspace(0, 1, int(nl)):
                    x = int(px + math.cos(na) * nl * s)
                    y = int(py + math.sin(na) * nl * s)
                    if 1 <= x < n - 1 and 1 <= y < n - 1:
                        col[y - 1:y + 2, x] = c * (0.8 + 0.3 * s)
                        alpha[y - 1:y + 2, x] = 1
                        hgt[y, x] = 1.0
        for s in np.linspace(0, 1, int(L)):  # twig
            x, y = int(sx + math.cos(ang) * L * s), int(sy + math.sin(ang) * L * s)
            if 2 <= x < n - 2 and 2 <= y < n - 2:
                col[y - 2:y + 3, x - 2:x + 3] = hexrgb("5A4030")
                alpha[y - 2:y + 3, x - 2:x + 3] = 1
    save("foliage_needles", col, alpha)
    save_normal("foliage_needles_n", normal_from_height(blur(hgt, 1), 2.0))


def make_clouds():
    """Sky cloud layer (white with alpha), tiles horizontally; tinted per lighting preset in Unity."""
    w, h = 2048, 512
    warp = fbm(h, w, base=3, octaves=4, seed=101, aspect=(1, 4))
    d = fbm(h, w, base=3, octaves=7, seed=102, aspect=(1, 4))
    d2 = np.roll(d, (int(20), int(60)), axis=(0, 1))
    dens = 0.6 * d + 0.4 * d2 + 0.25 * (warp - 0.5)
    yy = np.linspace(0, 1, h)[:, None]                           # row 0 = top
    band = np.clip(1 - np.abs(yy - 0.55) / 0.4, 0, 1)           # clouds sit in the middle band
    dens = np.clip((dens - 0.52) / 0.18, 0, 1) * band
    alpha = dens ** 0.8
    soft = blur(dens, 6)
    above = sum(np.roll(soft, k, axis=0) for k in (6, 12, 20, 30)) / 4   # cloud mass between here and the sun
    shade = np.clip(1.02 - 0.32 * above, 0.74, 1.0)                    # lit tops, grey undersides
    rgb = np.dstack([shade, shade * 0.99, shade * 0.97])
    save("clouds", rgb, alpha)


def rounded_rect(n_h, n_w, x0, y0, x1, y1, r):
    """Anti-aliased rounded-rectangle coverage (h, w) for the box [x0, x1) x [y0, y1)."""
    yy, xx = np.mgrid[0:n_h, 0:n_w] + 0.5
    cx = np.clip(xx, x0 + r, x1 - r)
    cy = np.clip(yy, y0 + r, y1 - r)
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) - r
    return np.clip(0.5 - d, 0, 1)


def make_icon():
    """App icon (macOS / Linux / Windows): a kraft parcel face with the red HANDLE WITH CARE stamp.
    Written to Assets/Icons/AppIcon.png (outside Resources: only the player settings use it)."""
    n = 1024
    out = os.path.join(ROOT, "Assets", "Icons")
    os.makedirs(out, exist_ok=True)
    # macOS-style tile: 824 px rounded square centred on the 1024 canvas, with a soft drop shadow
    m, r = 100, 185
    tile = rounded_rect(n, n, m, m, n - m, n - m, r)
    shadow = blur(rounded_rect(n, n, m, m + 14, n - m, n - m + 14, r), 14) * 0.35
    rgb, hgt = kraft_base(n, "C8955A", 41)
    # a strip of parcel tape across the top, slightly darker where it overlaps the edge
    tape = rounded_rect(n, n, m, 200, n - m, 300, 0)
    rgb = rgb * (1 - tape[..., None] * 0.55) + hexrgb("D9B26F")[None, None, :] * tape[..., None] * 0.55
    # the stamp: postal red with a cream inner rule and the two-line wordmark
    red = hexrgb("D9483B")
    cream = hexrgb("F3E9D2")
    sx0, sy0, sx1, sy1 = 170, 360, n - 170, 820
    stamp = rounded_rect(n, n, sx0, sy0, sx1, sy1, 46)
    rule = np.clip(rounded_rect(n, n, sx0 + 22, sy0 + 22, sx1 - 22, sy1 - 22, 30)
                   - rounded_rect(n, n, sx0 + 32, sy0 + 32, sx1 - 32, sy1 - 32, 22), 0, 1)
    ink = ink_wear((n, n), 19, amount=0.2) * 0.3 + 0.7
    a = stamp * ink
    rgb = rgb * (1 - a[..., None]) + red[None, None, :] * a[..., None]
    paste(rgb, None, rule * ink, cream, 0, 0)
    w1 = text_mask("HANDLE", width=600, height=190, letter_spacing=0.06)
    w2 = text_mask("WITH CARE", width=600, height=150, letter_spacing=0.08)
    paste(rgb, None, w1 * ink[0:190, 0:600], cream, (n - 600) // 2, sy0 + 50)
    paste(rgb, None, w2 * ink[200:350, 100:700], cream, (n - 600) // 2, sy0 + 250)
    # soft edge shading so the tile reads as a slightly domed card
    yy, xx = np.mgrid[0:n, 0:n] / n
    rgb = rgb * (1.04 - 0.1 * yy[..., None] - 0.04 * np.abs(xx - 0.5)[..., None])
    alpha = np.maximum(tile, shadow)
    rgb = rgb * tile[..., None] + np.zeros(3)[None, None, :] * (1 - tile[..., None])
    save("AppIcon", rgb, alpha, out=out)


ALL = {
    "kraft": make_kraft, "kraftin": make_kraftin, "corrugate": make_corrugate, "tapes": make_tapes,
    "label": make_label, "decals": make_decals, "wood": make_wood, "plaster": make_plaster,
    "pegboard": make_pegboard, "paper_ui": make_paper_ui,
    "asphalt": make_asphalt, "concrete": make_concrete, "grass": make_grass, "cobble": make_cobble,
    "brick": make_brick, "shingle": make_shingle, "treadplate": make_treadplate, "water": make_water,
    "hay": make_hay, "clouds": make_clouds, "icon": make_icon, "foliage": lambda: (make_foliage_broadleaf(), make_foliage_needles()),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name, fn in ALL.items():
        if argv and name not in argv:
            continue
        fn()


main()
