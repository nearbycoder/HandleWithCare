"""Physically based materials and texture baking for Handle With Care (Blender 4.5, Cycles).

Material names of the form  p_<kind>_<RRGGBB>  build a procedural Principled BSDF (see RECIPES):
real-world surface looks with colour variation, micro-normal detail and roughness variation, all
driven by object-space noise so they need no UVs. `bake()` then gives a model one non-overlapping
UV atlas and bakes those shaders (plus ambient occlusion) into three textures that Unity's URP Lit
shader reads directly:

    Textures/Baked/<model>.png        albedo (sRGB, AO lightly multiplied in)
    Textures/Baked/<model>_n.png      tangent-space normal map (OpenGL / Unity convention)
    Textures/Baked/<model>_mask.png   R metallic, G ambient occlusion, A smoothness (linear)

Baked faces end up sharing a single material named  bake_<model>. Faces using glass_ / glow_
materials are not baked and keep their names (Unity builds those at runtime).
"""
import math
import os

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BAKED_DIR = os.path.join(ROOT, "Assets", "Resources", "Textures", "Baked")


def _lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgb(h):
    h = h.lstrip("#")
    return tuple(_lin(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4))


def P(kind, h, h2=None):
    """Material name for a procedural PBR look (h2: second colour for two-tone recipes)."""
    name = f"p_{kind}_{h.lstrip('#').upper()}"
    return name + "_" + h2.lstrip("#").upper() if h2 else name


# ----------------------------------------------------------------------------- node helpers

class NB:
    def __init__(self, mat):
        self.nt = mat.node_tree
        self.nodes = self.nt.nodes
        self.links = self.nt.links
        self._co = None

    def node(self, type_, **inputs):
        nd = self.nodes.new(type_)
        for k, v in inputs.items():
            self.set(nd, k, v)
        return nd

    def set(self, nd, key, v):
        sock = nd.inputs[key]
        if isinstance(v, bpy.types.NodeSocket):
            self.links.new(v, sock)
        else:
            sock.default_value = v

    def coord(self):
        if self._co is None:
            self._co = self.node("ShaderNodeTexCoord").outputs["Object"]
        return self._co

    def mapped(self, scale=(1, 1, 1), offset=(0, 0, 0), rot=(0, 0, 0)):
        m = self.node("ShaderNodeMapping")
        self.set(m, "Vector", self.coord())
        m.inputs["Scale"].default_value = scale
        m.inputs["Location"].default_value = offset
        m.inputs["Rotation"].default_value = rot
        return m.outputs["Vector"]

    def noise(self, scale, detail=2.0, rough=0.5, vec=None, distortion=0.0, out="Fac", lacunarity=2.0):
        n = self.node("ShaderNodeTexNoise")
        n.noise_dimensions = "3D"
        self.set(n, "Vector", vec if vec is not None else self.coord())
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        n.inputs["Distortion"].default_value = distortion
        n.inputs["Lacunarity"].default_value = lacunarity
        return n.outputs[out]

    def voronoi(self, scale, feature="F1", vec=None, out="Distance", randomness=1.0, metric="EUCLIDEAN"):
        v = self.node("ShaderNodeTexVoronoi")
        v.voronoi_dimensions = "3D"
        v.feature = feature
        v.distance = metric
        self.set(v, "Vector", vec if vec is not None else self.coord())
        v.inputs["Scale"].default_value = scale
        v.inputs["Randomness"].default_value = randomness
        return v.outputs[out]

    def wave(self, scale, axis="Z", kind="BANDS", distortion=0.0, detail=2.0, vec=None, profile="SIN"):
        w = self.node("ShaderNodeTexWave")
        w.wave_type = kind
        if kind == "BANDS":
            w.bands_direction = axis
        else:
            w.rings_direction = axis
        w.wave_profile = profile
        self.set(w, "Vector", vec if vec is not None else self.coord())
        w.inputs["Scale"].default_value = scale
        w.inputs["Distortion"].default_value = distortion
        w.inputs["Detail"].default_value = detail
        return w.outputs["Fac"]

    def math(self, op, a, b=0.0, clamp=False):
        m = self.node("ShaderNodeMath")
        m.operation = op
        m.use_clamp = clamp
        self.set(m, 0, a)
        self.set(m, 1, b)
        return m.outputs[0]

    def remap(self, v, lo, hi, in_lo=0.0, in_hi=1.0):
        m = self.node("ShaderNodeMapRange")
        m.clamp = True
        self.set(m, "Value", v)
        m.inputs["From Min"].default_value = in_lo
        m.inputs["From Max"].default_value = in_hi
        m.inputs["To Min"].default_value = lo
        m.inputs["To Max"].default_value = hi
        return m.outputs["Result"]

    def mix(self, fac, a, b):
        m = self.node("ShaderNodeMix")
        m.data_type = "RGBA"
        m.blend_type = "MIX"
        self.set(m, "Factor", fac)
        self.set(m, 6, a if isinstance(a, bpy.types.NodeSocket) else (*a, 1.0))
        self.set(m, 7, b if isinstance(b, bpy.types.NodeSocket) else (*b, 1.0))
        return m.outputs[2]

    def vary(self, color, fac, amount, lo_fac=None):
        """Blend between a darker and a lighter version of `color` by fac (0..1)."""
        lo = tuple(c * (1 - amount) for c in color)
        hi = tuple(min(1.0, c * (1 + amount)) for c in color)
        return self.mix(fac, lo, hi)

    def add(self, a, b):
        return self.math("ADD", a, b)

    def mul(self, a, b):
        return self.math("MULTIPLY", a, b)


# ----------------------------------------------------------------------------- recipes
# Each recipe returns dict(color, rough, metal, height, bump, bump_dist). Values may be sockets.
# Object space is in metres; items are roughly 0.1-0.5 m across.

def _ceramic(nb, c, gloss=0.06):
    pool = nb.noise(5, 3, 0.55)
    color = nb.vary(c, pool, 0.035)
    rough = nb.remap(nb.noise(30, 2), gloss * 0.7, gloss * 1.8)
    height = nb.noise(18, 3, 0.5)
    return dict(color=color, rough=rough, metal=0.0, height=height, bump=0.035, bump_dist=0.0008)


def _gold(nb, c):
    wear = nb.noise(60, 4, 0.6)
    return dict(color=nb.vary(c, wear, 0.08), rough=nb.remap(wear, 0.12, 0.32), metal=1.0,
                height=nb.noise(400, 2), bump=0.03, bump_dist=0.0003)


def _chrome(nb, c):
    smear = nb.noise(25, 3, 0.6)
    return dict(color=c, rough=nb.remap(smear, 0.04, 0.12), metal=1.0, height=None)


def _brushed(nb, c):
    streak = nb.noise(1.0, 3, 0.6, vec=nb.mapped(scale=(3, 900, 3)))
    return dict(color=nb.vary(c, streak, 0.05), rough=nb.remap(streak, 0.18, 0.36), metal=1.0,
                height=streak, bump=0.05, bump_dist=0.0002)


def _tin(nb, c):
    """Painted tinplate: slightly metallic lacquer with scuffs."""
    scuff = nb.math("POWER", nb.noise(90, 6, 0.7), 4.0)
    scratches = nb.math("LESS_THAN", nb.voronoi(140, "DISTANCE_TO_EDGE"), 0.012)
    s = nb.math("MAXIMUM", nb.mul(scuff, 3.0), nb.mul(scratches, 0.5))
    return dict(color=nb.vary(c, nb.noise(12, 3), 0.06), rough=nb.remap(s, 0.22, 0.55), metal=0.75,
                height=nb.noise(300, 2), bump=0.03, bump_dist=0.0003)


def _plastic(nb, c, gloss=0.3):
    peel = nb.noise(260, 3, 0.5)
    return dict(color=nb.vary(c, nb.noise(8, 2), 0.03), rough=nb.remap(nb.noise(20, 2), gloss * 0.8, gloss * 1.3), metal=0.0,
                height=peel, bump=0.04, bump_dist=0.0002)


def _lacquer(nb, c):
    return _plastic(nb, c, 0.1)


def _rubber(nb, c):
    return dict(color=nb.vary(c, nb.noise(15, 3), 0.05), rough=nb.remap(nb.noise(40, 3), 0.5, 0.7), metal=0.0,
                height=nb.noise(500, 3), bump=0.08, bump_dist=0.0002)


def _plush(nb, c):
    tufts = nb.voronoi(160, "F1", vec=nb.mapped(scale=(1, 1, 0.6)))
    clump = nb.noise(70, 5, 0.65, distortion=0.6)
    fibre = nb.noise(1100, 8, 0.75, distortion=0.8, vec=nb.mapped(scale=(1, 1, 0.45)))
    tone = nb.add(nb.add(nb.mul(clump, 0.45), nb.mul(fibre, 0.35)), nb.mul(nb.math("SUBTRACT", 1.0, tufts), 0.2))
    return dict(color=nb.vary(c, tone, 0.22), rough=nb.remap(fibre, 0.85, 1.0), metal=0.0,
                height=nb.add(nb.add(nb.mul(fibre, 0.55), nb.mul(clump, 0.2)), nb.mul(nb.math("SUBTRACT", 1.0, tufts), 0.25)),
                bump=0.7, bump_dist=0.002)


def _swirl(nb, c, c2):
    """Marbled resin / swirled rubber: two colours folded through each other."""
    warp = nb.noise(6, 4, 0.6, distortion=4.0)
    bands = nb.wave(9, "X", distortion=14.0, detail=4)
    fac = nb.remap(nb.add(nb.mul(warp, 0.5), nb.mul(bands, 0.5)), 0, 1, 0.35, 0.65)
    return dict(color=nb.mix(fac, c, c2), rough=nb.remap(nb.noise(30, 3), 0.06, 0.14), metal=0.0,
                height=nb.noise(200, 2), bump=0.01, bump_dist=0.0003)


def _stripes(nb, c, c2):
    """Candy-striped wax (twisted candle stripes)."""
    s = nb.wave(55, "Z", kind="BANDS", profile="SAW", distortion=0.0, vec=nb.mapped(rot=(0.0, 0.5, 0.0)))
    fac = nb.math("GREATER_THAN", s, 0.5)
    return dict(color=nb.mix(fac, c, c2), rough=nb.remap(nb.noise(50, 3), 0.3, 0.45), metal=0.0,
                height=nb.noise(120, 3), bump=0.05, bump_dist=0.0004)


def _felt(nb, c):
    fibre = nb.noise(1200, 6, 0.7)
    return dict(color=nb.vary(c, nb.noise(40, 4), 0.07), rough=0.97, metal=0.0,
                height=fibre, bump=0.3, bump_dist=0.0006)


def _cloth(nb, c):
    """Book cloth / woven fabric."""
    weft = nb.wave(900, "X", profile="SIN")
    warp = nb.wave(900, "Z", profile="SIN")
    weave = nb.mul(nb.add(weft, warp), 0.5)
    grime = nb.noise(20, 4, 0.6)
    return dict(color=nb.vary(c, nb.add(nb.mul(grime, 0.7), nb.mul(weave, 0.3)), 0.12), rough=nb.remap(grime, 0.62, 0.82),
                metal=0.0, height=weave, bump=0.25, bump_dist=0.0003)


def _pages(nb, c):
    lines = nb.wave(1400, "Z", profile="SIN", distortion=1.5, detail=1)
    return dict(color=nb.vary(c, nb.add(nb.mul(lines, 0.6), nb.mul(nb.noise(30, 3), 0.4)), 0.1), rough=0.88,
                metal=0.0, height=lines, bump=0.25, bump_dist=0.0003)


def _paper(nb, c):
    fibre = nb.noise(700, 6, 0.65)
    blotch = nb.noise(25, 4, 0.6)
    return dict(color=nb.vary(c, nb.add(nb.mul(blotch, 0.6), nb.mul(fibre, 0.4)), 0.06), rough=nb.remap(fibre, 0.8, 0.95),
                metal=0.0, height=fibre, bump=0.15, bump_dist=0.0003)


def _terracotta(nb, c):
    grain = nb.noise(900, 4, 0.7)
    speck = nb.math("LESS_THAN", nb.voronoi(220), 0.06)
    tone = nb.noise(12, 4, 0.6)
    color = nb.vary(c, tone, 0.12)
    color = nb.mix(nb.mul(speck, 0.55), color, tuple(x * 0.45 for x in c))
    return dict(color=color, rough=nb.remap(grain, 0.8, 0.95), metal=0.0, height=nb.add(grain, nb.mul(speck, -0.5)),
                bump=0.35, bump_dist=0.0006)


def _soil(nb, c):
    crumbs = nb.voronoi(260, "F1")
    tone = nb.noise(80, 5, 0.7)
    return dict(color=nb.vary(c, nb.add(nb.mul(tone, 0.6), nb.mul(crumbs, 0.4)), 0.4), rough=0.95, metal=0.0,
                height=nb.math("SUBTRACT", 1.0, crumbs), bump=0.9, bump_dist=0.002)


def _cactus(nb, c):
    ribs = nb.noise(200, 3, 0.5)
    mottling = nb.noise(18, 4, 0.6)
    return dict(color=nb.vary(c, mottling, 0.14), rough=nb.remap(ribs, 0.38, 0.55), metal=0.0,
                height=nb.noise(600, 4, 0.6), bump=0.15, bump_dist=0.0003)


def _cork(nb, c):
    pits = nb.voronoi(320, "F1")
    tone = nb.noise(90, 5, 0.7)
    color = nb.vary(c, nb.add(nb.mul(tone, 0.5), nb.mul(pits, 0.5)), 0.28)
    return dict(color=color, rough=0.92, metal=0.0, height=pits, bump=0.6, bump_dist=0.0008)


def _wood(nb, c, gloss=0.3):
    rings = nb.wave(60, "Z", kind="RINGS", distortion=6.0, detail=3, vec=nb.mapped(scale=(1, 1, 0.15)))
    fine = nb.noise(400, 3, 0.6, vec=nb.mapped(scale=(1, 1, 0.05)))
    tone = nb.add(nb.mul(rings, 0.65), nb.mul(fine, 0.35))
    return dict(color=nb.vary(c, tone, 0.22), rough=nb.remap(fine, gloss * 0.8, gloss * 1.4), metal=0.0,
                height=fine, bump=0.08, bump_dist=0.0003)


def _frosting(nb, c):
    lumps = nb.noise(35, 3, 0.55)
    return dict(color=nb.vary(c, lumps, 0.05), rough=nb.remap(lumps, 0.28, 0.45), metal=0.0,
                height=lumps, bump=0.35, bump_dist=0.0015)


def _sponge(nb, c):
    holes = nb.voronoi(260, "F1")
    return dict(color=nb.vary(c, nb.add(nb.mul(holes, 0.7), nb.mul(nb.noise(20, 3), 0.3)), 0.2), rough=0.95, metal=0.0,
                height=holes, bump=0.8, bump_dist=0.001)


def _skin(nb, c):
    """Moist amphibian skin: mottled, bumpy, glossy."""
    warts = nb.voronoi(110, "F1")
    mottle = nb.noise(14, 5, 0.65)
    spots = nb.math("LESS_THAN", nb.noise(9, 2, 0.5), 0.38)
    color = nb.vary(c, mottle, 0.18)
    color = nb.mix(nb.mul(spots, 0.6), color, tuple(x * 0.45 for x in c))
    return dict(color=color, rough=nb.remap(nb.noise(60, 3), 0.18, 0.4), metal=0.0,
                height=nb.math("SUBTRACT", 1.0, warts), bump=0.25, bump_dist=0.0008)


def _scales(nb, c):
    cells = nb.voronoi(150, "F1", metric="EUCLIDEAN")
    edges = nb.voronoi(150, "DISTANCE_TO_EDGE")
    tone = nb.noise(10, 4, 0.6)
    color = nb.vary(c, nb.add(nb.mul(tone, 0.5), nb.mul(nb.remap(edges, 0, 1, 0, 0.12), 0.5)), 0.25)
    return dict(color=color, rough=nb.remap(cells, 0.3, 0.55), metal=0.0,
                height=nb.remap(edges, 0, 1, 0, 0.08), bump=0.5, bump_dist=0.001)


def _eggshell(nb, c):
    plates = nb.voronoi(40, "DISTANCE_TO_EDGE")
    tone = nb.noise(16, 5, 0.6)
    color = nb.vary(c, nb.add(nb.mul(tone, 0.6), nb.mul(nb.remap(plates, 0, 1, 0, 0.1), 0.4)), 0.25)
    return dict(color=color, rough=nb.remap(tone, 0.15, 0.3), metal=0.1,
                height=nb.remap(plates, 0, 1, 0, 0.05), bump=0.4, bump_dist=0.001)


def _armour(nb, c):
    """Armadillo plates: keratin scutes with tiny bumps."""
    scutes = nb.voronoi(260, "F1")
    tone = nb.noise(25, 4, 0.6)
    return dict(color=nb.vary(c, nb.add(nb.mul(tone, 0.6), nb.mul(scutes, 0.4)), 0.18), rough=nb.remap(scutes, 0.45, 0.65),
                metal=0.0, height=nb.math("SUBTRACT", 1.0, scutes), bump=0.35, bump_dist=0.0008)


def _flesh(nb, c):
    """Soft bare skin (armadillo face and belly)."""
    pores = nb.noise(800, 4, 0.6)
    return dict(color=nb.vary(c, nb.noise(20, 4), 0.08), rough=nb.remap(pores, 0.45, 0.6), metal=0.0,
                height=pores, bump=0.12, bump_dist=0.0003)


def _foam(nb, c):
    cells = nb.voronoi(1100, "F1")
    return dict(color=nb.vary(c, nb.add(nb.mul(cells, 0.6), nb.mul(nb.noise(30, 3), 0.4)), 0.12), rough=0.98, metal=0.0,
                height=cells, bump=0.6, bump_dist=0.0006)


def _wax(nb, c):
    return dict(color=nb.vary(c, nb.noise(30, 3), 0.05), rough=nb.remap(nb.noise(50, 3), 0.3, 0.45), metal=0.0,
                height=nb.noise(120, 3), bump=0.05, bump_dist=0.0004)


def _bead(nb, c):
    """Glossy glass eye / button."""
    return dict(color=c, rough=0.04, metal=0.0, height=None)


def _paint(nb, c):
    return dict(color=nb.vary(c, nb.noise(10, 3), 0.04), rough=nb.remap(nb.noise(60, 3), 0.35, 0.5), metal=0.0,
                height=nb.noise(350, 3), bump=0.04, bump_dist=0.0002)


def _matte(nb, c):
    return dict(color=nb.vary(c, nb.noise(20, 3), 0.06), rough=0.85, metal=0.0, height=nb.noise(500, 3), bump=0.08, bump_dist=0.0003)


def _webbing(nb, c):
    """Woven nylon strap."""
    ribs = nb.wave(1600, "X", profile="SIN")
    return dict(color=nb.vary(c, nb.add(nb.mul(ribs, 0.5), nb.mul(nb.noise(30, 3), 0.5)), 0.12), rough=0.6, metal=0.0,
                height=ribs, bump=0.3, bump_dist=0.0003)


def _cardboard(nb, c):
    fibre = nb.noise(500, 6, 0.65)
    blotch = nb.noise(12, 4, 0.6)
    return dict(color=nb.vary(c, nb.add(nb.mul(blotch, 0.7), nb.mul(fibre, 0.3)), 0.12), rough=0.9, metal=0.0,
                height=fibre, bump=0.2, bump_dist=0.0004)


def _liquid(nb, c):
    return dict(color=c, rough=0.02, metal=0.0, height=nb.noise(30, 2), bump=0.02, bump_dist=0.0005)


def _satin(nb, c):
    sheen = nb.wave(2500, "X", profile="SIN")
    return dict(color=nb.vary(c, nb.noise(15, 3), 0.08), rough=nb.remap(sheen, 0.22, 0.38), metal=0.0,
                height=sheen, bump=0.1, bump_dist=0.0002)


def _thread(nb, c):
    twist = nb.wave(3000, "X", kind="BANDS", distortion=2.0)
    return dict(color=nb.vary(c, twist, 0.15), rough=0.8, metal=0.0, height=twist, bump=0.3, bump_dist=0.0002)


# ---- scenery (props are 0.5-6 m, so features are coarser than the items')

def _carpaint(nb, c):
    peel = nb.noise(90, 3, 0.5)
    return dict(color=nb.vary(c, nb.noise(4, 2), 0.025), rough=nb.remap(nb.noise(12, 3), 0.1, 0.2), metal=0.15,
                height=peel, bump=0.03, bump_dist=0.0008)


def _enamel(nb, c):
    chips = nb.math("GREATER_THAN", nb.math("POWER", nb.noise(14, 6, 0.7), 3.0), 0.32)
    return dict(color=nb.mix(chips, (*c,), (0.32, 0.3, 0.29)), rough=nb.remap(nb.noise(25, 4), 0.25, 0.45), metal=0.0,
                height=nb.add(nb.noise(120, 3), nb.mul(chips, -0.6)), bump=0.05, bump_dist=0.001)


def _bark(nb, c):
    ridges = nb.wave(9, "X", kind="BANDS", distortion=9.0, detail=4, vec=nb.mapped(scale=(1, 1, 0.12)))
    grit = nb.noise(60, 5, 0.7)
    tone = nb.add(nb.mul(ridges, 0.7), nb.mul(grit, 0.3))
    return dict(color=nb.vary(c, tone, 0.45), rough=0.92, metal=0.0, height=tone, bump=0.9, bump_dist=0.01)


def _foliage(nb, c):
    leaf = nb.voronoi(26, "F1", out="Color", vec=nb.mapped(scale=(1, 1, 1.2)))
    edge = nb.voronoi(26, "F1", out="Distance", vec=nb.mapped(scale=(1, 1, 1.2)))
    sun = nb.noise(1.5, 3, 0.6)
    tone = nb.add(nb.mul(leaf, 0.55), nb.mul(sun, 0.45))
    return dict(color=nb.vary(c, tone, 0.42), rough=nb.remap(leaf, 0.45, 0.75), metal=0.0,
                height=nb.math("SUBTRACT", 1.0, edge), bump=0.9, bump_dist=0.015)


def _needles(nb, c):
    tufts = nb.voronoi(40, "F1", out="Color", vec=nb.mapped(scale=(1, 1, 0.5)))
    fine = nb.noise(140, 4, 0.7, vec=nb.mapped(scale=(1, 1, 0.3)))
    tone = nb.add(nb.mul(tufts, 0.5), nb.mul(fine, 0.5))
    return dict(color=nb.vary(c, tone, 0.4), rough=0.8, metal=0.0, height=fine, bump=0.8, bump_dist=0.01)


def _burlap(nb, c):
    weft = nb.wave(160, "X", profile="SIN")
    warp = nb.wave(160, "Z", profile="SIN")
    weave = nb.mul(nb.add(weft, warp), 0.5)
    return dict(color=nb.vary(c, nb.add(nb.mul(nb.noise(6, 4), 0.6), nb.mul(weave, 0.4)), 0.2), rough=0.95, metal=0.0,
                height=weave, bump=0.5, bump_dist=0.002)


def _gingham(nb, c, c2):
    cx = nb.math("GREATER_THAN", nb.wave(45, "X", profile="SIN"), 0.5)
    cz = nb.math("GREATER_THAN", nb.wave(45, "Y", profile="SIN"), 0.5)
    tone = nb.mul(nb.add(cx, cz), 0.5)
    weave = nb.mul(nb.add(nb.wave(700, "X"), nb.wave(700, "Y")), 0.5)
    return dict(color=nb.mix(tone, c2, c), rough=0.85, metal=0.0, height=weave, bump=0.2, bump_dist=0.0006)


def _gingham_v(nb, c, c2):
    """Gingham for vertical faces (checks across x and height)."""
    cx = nb.math("GREATER_THAN", nb.wave(45, "X", profile="SIN"), 0.5)
    cz = nb.math("GREATER_THAN", nb.wave(45, "Z", profile="SIN"), 0.5)
    tone = nb.mul(nb.add(cx, cz), 0.5)
    weave = nb.mul(nb.add(nb.wave(700, "X"), nb.wave(700, "Z")), 0.5)
    return dict(color=nb.mix(tone, c2, c), rough=0.85, metal=0.0, height=weave, bump=0.2, bump_dist=0.0006)


def _rope(nb, c):
    twist = nb.wave(60, "Z", kind="BANDS", distortion=1.0, vec=nb.mapped(rot=(0.6, 0.6, 0)))
    return dict(color=nb.vary(c, nb.add(nb.mul(twist, 0.6), nb.mul(nb.noise(30, 3), 0.4)), 0.25), rough=0.9, metal=0.0,
                height=twist, bump=0.6, bump_dist=0.003)


def _timber(nb, c):
    """Rough sawn, weathered planks (catapult, crates)."""
    grain = nb.noise(2.0, 6, 0.65, vec=nb.mapped(scale=(30, 1.2, 30)), distortion=0.4)
    knots = nb.math("POWER", nb.noise(6, 3, 0.5), 6.0)
    tone = nb.add(nb.mul(grain, 0.8), nb.mul(knots, 2.0))
    return dict(color=nb.vary(c, tone, 0.3), rough=0.85, metal=0.0, height=grain, bump=0.5, bump_dist=0.004)


TWO_TONE = {"swirl", "stripes", "gingham", "ginghamv"}

RECIPES = {
    "liquid": _liquid, "satin": _satin, "thread": _thread, "swirl": _swirl, "stripes": _stripes,
    "ceramic": _ceramic, "porcelain": lambda nb, c: _ceramic(nb, c, 0.04), "gold": _gold, "chrome": _chrome,
    "brushed": _brushed, "tin": _tin, "plastic": _plastic, "lacquer": _lacquer, "rubber": _rubber, "plush": _plush,
    "felt": _felt, "cloth": _cloth, "pages": _pages, "paper": _paper, "terracotta": _terracotta, "soil": _soil,
    "cactus": _cactus, "cork": _cork, "wood": _wood, "frosting": _frosting, "sponge": _sponge, "skin": _skin,
    "scales": _scales, "eggshell": _eggshell, "armour": _armour, "flesh": _flesh, "foam": _foam, "wax": _wax,
    "bead": _bead, "paint": _paint, "matte": _matte, "webbing": _webbing, "cardboard": _cardboard,
    "varnish": lambda nb, c: _wood(nb, c, 0.18),
    "carpaint": _carpaint, "enamel": _enamel, "bark": _bark, "foliage": _foliage, "needles": _needles,
    "burlap": _burlap, "gingham": _gingham, "ginghamv": _gingham_v, "rope": _rope, "timber": _timber,
}


def build_material(name):
    """Create the procedural material for a p_<kind>_<HEX>[_<HEX2>] name."""
    parts = name.split("_")
    kind = parts[1]
    c = rgb(parts[2][:6])
    c2 = rgb(parts[3][:6]) if len(parts) > 3 else None
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nb = NB(mat)
    for n in list(nb.nodes):
        nb.nodes.remove(n)
    out = nb.node("ShaderNodeOutputMaterial")
    bsdf = nb.node("ShaderNodeBsdfPrincipled")
    r = RECIPES[kind](nb, c, c2) if kind in TWO_TONE else RECIPES[kind](nb, c)
    nb.set(bsdf, "Base Color", r["color"] if isinstance(r["color"], bpy.types.NodeSocket) else (*r["color"], 1.0))
    nb.set(bsdf, "Roughness", r["rough"])
    nb.set(bsdf, "Metallic", r["metal"])
    if r.get("height") is not None:
        bump = nb.node("ShaderNodeBump")
        bump.inputs["Strength"].default_value = r["bump"]
        bump.inputs["Distance"].default_value = r["bump_dist"]
        nb.set(bump, "Height", r["height"])
        nb.set(bsdf, "Normal", bump.outputs["Normal"])
    nb.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    mat.diffuse_color = (*c, 1.0)
    mat["pbr"] = True
    return mat


# ----------------------------------------------------------------------------- baking

def _is_baked(mat):
    return mat is not None and not mat.name.startswith(("glass_", "glow_", "tex_", "decal_", "leaf_"))


def _setup_cycles(samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    sc.render.bake.margin = 12
    sc.render.bake.margin_type = "EXTEND"
    sc.render.bake.use_clear = True
    sc.render.threads_mode = "AUTO"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("bakeworld")
    sc.world.use_nodes = True
    sc.world.light_settings.distance = 0.06


def _image(name, res):
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, res, res, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    return img


def _pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def _save_png(path, arr):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("__out", w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def _to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def _unwrap(obj, margin, bake_faces):
    """New atlas for the faces that get baked; every other face keeps its authored UVs (tex_/decal_)."""
    import bmesh
    me = obj.data
    old = None
    if len(me.uv_layers) > 0:
        old = np.empty(len(me.loops) * 2, dtype=np.float32)
        me.uv_layers[0].data.foreach_get("uv", old)
    while len(me.uv_layers) > 0:
        me.uv_layers.remove(me.uv_layers[0])
    layer = me.uv_layers.new(name="UVMap")
    if old is not None:
        layer.data.foreach_set("uv", old)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bm = bmesh.from_edit_mesh(me)
    for f in bm.faces:
        f.select_set(False)
    for f in bm.faces:
        if bake_faces[f.index]:
            f.select_set(True)
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    try:
        bpy.ops.uv.pack_islands(margin=margin, rotate=True, shape_method="CONCAVE")
    except TypeError:
        bpy.ops.uv.pack_islands(margin=margin, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def _bake_emit(obj, mats, img, socket_name, samples):
    """Bake one Principled input (colour or value) by routing it to an emission shader."""
    saved = []
    for m in mats:
        nt = m.node_tree
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        old = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Strength"].default_value = 1.0
        src = bsdf.inputs[socket_name] if bsdf else None
        if src is not None and src.links:
            nt.links.new(src.links[0].from_socket, em.inputs["Color"])
        elif src is not None:
            v = src.default_value
            em.inputs["Color"].default_value = tuple(v) if hasattr(v, "__len__") else (v, v, v, 1.0)
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        saved.append((nt, out, old, em))
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.bake(type="EMIT")
    for nt, out, old, em in saved:
        nt.nodes.remove(em)
        if old is not None:
            nt.links.new(old, out.inputs["Surface"])


def bake(obj, res=1024, ao_samples=96, ao_strength=0.55, margin=0.004, name=None):
    """Unwrap `obj`, bake its p_ materials into textures, and swap them for bake_<name> (default obj.name)."""
    os.makedirs(BAKED_DIR, exist_ok=True)
    name = name or obj.name
    res = int(obj.get("bake_res", res))
    mats = [s.material for s in obj.material_slots]
    baked = [m for m in mats if _is_baked(m)]
    if not baked:
        return
    _setup_cycles(4)
    fidx = np.empty(len(obj.data.polygons), dtype=np.int32)
    obj.data.polygons.foreach_get("material_index", fidx)
    _unwrap(obj, margin, [_is_baked(mats[i]) if i < len(mats) else True for i in fidx])

    img = _image("__bake_" + name, res)
    dummy = _image("__bake_dummy", 16)                     # target for faces that are not baked
    for m in mats:
        nt = m.node_tree
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = img if _is_baked(m) else dummy
        tn.name = "__bake_target"
        for n in nt.nodes:
            n.select = False
        tn.select = True
        nt.nodes.active = tn

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    # leaf cards and other marked helpers would smother the occlusion bake; hide them meanwhile
    hidden = [o for o in bpy.context.scene.objects if o.get("bake_hide") and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    _bake_emit(obj, mats, img, "Base Color", 4)
    albedo = _pixels(img)[..., :3].copy()
    _bake_emit(obj, mats, img, "Metallic", 1)
    metal = _pixels(img)[..., 0].copy()
    bpy.context.scene.cycles.samples = 4
    bpy.ops.object.bake(type="ROUGHNESS")
    rough = _pixels(img)[..., 0].copy()
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    normal = _pixels(img)[..., :3].copy()
    bpy.context.scene.cycles.samples = ao_samples
    bpy.ops.object.bake(type="AO")
    ao = _pixels(img)[..., 0].copy()
    for o in hidden:
        o.hide_render = False

    for m in mats:
        tn = m.node_tree.nodes.get("__bake_target")
        if tn:
            m.node_tree.nodes.remove(tn)
    bpy.data.images.remove(img)
    bpy.data.images.remove(dummy)

    shade = 1.0 - ao_strength * (1.0 - ao)
    alb = _to_srgb(albedo * shade[..., None])
    ones = np.ones_like(ao)
    base = os.path.join(BAKED_DIR, name)
    _save_png(base + ".png", np.dstack([alb, ones]))
    _save_png(base + "_n.png", np.dstack([normal, ones]))
    _save_png(base + "_mask.png", np.dstack([metal, ao, np.zeros_like(ao), 1.0 - rough]))

    # one material for every baked face; previews and the .blend show the baked result
    bm_mat = preview_material(name, base)
    idx_map = {}
    new_slots = [bm_mat]
    for i, m in enumerate(mats):
        if _is_baked(m):
            idx_map[i] = 0
        else:
            if m not in new_slots:
                new_slots.append(m)
            idx_map[i] = new_slots.index(m)
    face_idx = np.empty(len(obj.data.polygons), dtype=np.int32)
    obj.data.polygons.foreach_get("material_index", face_idx)
    face_idx = np.vectorize(lambda i: idx_map.get(int(i), 0))(face_idx).astype(np.int32) if len(face_idx) else face_idx
    obj.data.materials.clear()
    for m in new_slots:
        obj.data.materials.append(m)
    obj.data.polygons.foreach_set("material_index", face_idx)
    obj.data.update()
    print(f"[bake] {name}: {res}px, {len(obj.data.polygons)} faces")


def preview_material(name, base):
    mname = "bake_" + name
    old = bpy.data.materials.get(mname)
    if old:
        bpy.data.materials.remove(old)
    mat = bpy.data.materials.new(mname)
    mat.use_nodes = True
    nb = NB(mat)
    bsdf = next(n for n in nb.nodes if n.type == "BSDF_PRINCIPLED")

    def load(path, colorspace):
        img = bpy.data.images.load(path, check_existing=False)
        img.colorspace_settings.name = colorspace
        img.alpha_mode = "CHANNEL_PACKED"
        t = nb.node("ShaderNodeTexImage")
        t.image = img
        return t

    alb = load(base + ".png", "sRGB")
    nb.set(bsdf, "Base Color", alb.outputs["Color"])
    mask = load(base + "_mask.png", "Non-Color")
    sep = nb.node("ShaderNodeSeparateColor")
    nb.set(sep, "Color", mask.outputs["Color"])
    nb.set(bsdf, "Metallic", sep.outputs["Red"])
    nb.set(bsdf, "Roughness", nb.math("SUBTRACT", 1.0, mask.outputs["Alpha"]))
    nrm = load(base + "_n.png", "Non-Color")
    nm = nb.node("ShaderNodeNormalMap")
    nb.set(nm, "Color", nrm.outputs["Color"])
    nb.set(bsdf, "Normal", nm.outputs["Normal"])
    return mat
